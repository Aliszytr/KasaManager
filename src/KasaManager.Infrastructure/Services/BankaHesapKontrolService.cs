#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// Banka Hesap Kontrol modülü — ana servis implementasyonu.
/// Karşılaştırma raporlarından otomatik analiz, gün arası eşleştirme,
/// kullanıcı onayı ve Sabah Kasa auto-fill sağlar.
/// </summary>
public sealed partial class BankaHesapKontrolService : IBankaHesapKontrolService
{
    private readonly KasaManagerDbContext _db;
    private readonly IComparisonService _comparison;
    private readonly IImportOrchestrator _import;
    private readonly ILogger<BankaHesapKontrolService> _logger;

    public BankaHesapKontrolService(
        KasaManagerDbContext db,
        IComparisonService comparison,
        IImportOrchestrator import,
        ILogger<BankaHesapKontrolService> logger)
    {
        _db = db;
        _comparison = comparison;
        _import = import;
        _logger = logger;
    }

    // ═════════════════════════════════════════════════════════════
    // B2: AnalyzeFromComparison
    // ═════════════════════════════════════════════════════════════

    public async Task<HesapKontrolRapor> AnalyzeFromComparisonAsync(
        DateOnly analizTarihi,
        string uploadFolder,
        CancellationToken ct = default)
    {
        _logger.LogInformation("HesapKontrol analiz başlıyor: {Tarih}", analizTarihi);

        // ═══════════════════════════════════════════════════════════
        // ADIM 1: Karşılaştırmaları çalıştır, aday kayıtları oluştur
        // ═══════════════════════════════════════════════════════════
        var adayKayitlar = new List<HesapKontrolKaydi>();
        bool analizBasarili = false;

        // ─── Tahsilat-Masraf Karşılaştırma ───
        try
        {
            var tahsilatResult = await _comparison.CompareTahsilatMasrafAsync(uploadFolder, ct: ct);
            if (tahsilatResult.Ok && tahsilatResult.Value != null)
            {
                adayKayitlar.AddRange(ConvertToKayitlar(tahsilatResult.Value, BankaHesapTuru.Tahsilat, analizTarihi, "TahsilatMasraf"));
                analizBasarili = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tahsilat-Masraf karşılaştırma başarısız (dosyalar eksik olabilir)");
        }

        // ─── Harcama-Harç Karşılaştırma ───
        try
        {
            var harcResult = await _comparison.CompareHarcamaHarcAsync(uploadFolder, ct: ct);
            if (harcResult.Ok && harcResult.Value != null)
            {
                adayKayitlar.AddRange(ConvertToKayitlar(harcResult.Value, BankaHesapTuru.Harc, analizTarihi, "HarcamaHarc"));
                analizBasarili = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Harcama-Harç karşılaştırma başarısız (dosyalar eksik olabilir)");
        }

        // ─── Stopaj Virman Kontrolü ───
        var tumVirmanlar = adayKayitlar
            .Where(x => x.TespitEdilenTip == "VIRMAN")
            .Select(x => x.Tutar)
            .ToList();

        var stopajDurum = new StopajVirmanDurum(false, 0, null, "Reddiyat verisi yok");
        try
        {
            var reddiyatResult = await _comparison.CompareReddiyatCikisAsync(uploadFolder, ct: ct);
            if (reddiyatResult.Ok && reddiyatResult.Value != null)
            {
                var toplamStopaj = reddiyatResult.Value.TotalStopaj;
                var rSurplus = reddiyatResult.Value.SurplusBankaRecords;

                foreach (var v in rSurplus.Where(x => x.DetectedType == "VIRMAN"))
                {
                    tumVirmanlar.Add(Math.Abs(v.Tutar));
                }

                stopajDurum = CheckStopajFromAllVirmans(toplamStopaj, tumVirmanlar);

                adayKayitlar.Add(new HesapKontrolKaydi
                {
                    AnalizTarihi = analizTarihi,
                    HesapTuru = BankaHesapTuru.Stopaj,
                    Yon = stopajDurum.VirmanYapildiMi ? KayitYonu.Fazla : KayitYonu.Eksik,
                    Tutar = stopajDurum.BeklenenTutar,
                    Aciklama = stopajDurum.Mesaj,
                    Sinif = stopajDurum.VirmanYapildiMi ? FarkSinifi.Beklenen : FarkSinifi.Askida,
                    TespitEdilenTip = stopajDurum.VirmanYapildiMi ? "STOPAJ_VIRMAN_OK" : "STOPAJ_VIRMAN_BEKLIYOR",
                    KarsilastirmaTuru = "ReddiyatCikis",
                    Durum = stopajDurum.VirmanYapildiMi ? KayitDurumu.Cozuldu : KayitDurumu.Acik,
                    CozulmeTarihi = stopajDurum.VirmanYapildiMi ? analizTarihi : null,
                    Notlar = stopajDurum.BulunanVirmanTutar.HasValue
                        ? $"Beklenen: {stopajDurum.BeklenenTutar:N2}₺, Bulunan: {stopajDurum.BulunanVirmanTutar:N2}₺"
                        : $"Beklenen: {stopajDurum.BeklenenTutar:N2}₺"
                });
                analizBasarili = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reddiyat karşılaştırma başarısız (dosyalar eksik olabilir)");
        }

        // Analiz başarısız olduysa mevcut kayıtlara hiç dokunma
        if (!analizBasarili)
        {
            _logger.LogInformation("HesapKontrol: Hiçbir karşılaştırma başarılı olmadı, mevcut kayıtlar korunuyor.");
            var crossDayResult = await CrossDayReconcileAsync(analizTarihi, ct);
            return new HesapKontrolRapor(analizTarihi, 0, 0, 0, 0, 0, stopajDurum, crossDayResult.KesirEslesmeler, crossDayResult.PotansiyelEslesmeler,
                "Karşılaştırma dosyaları bulunamadı, mevcut kayıtlar korundu.");
        }

        // ═══════════════════════════════════════════════════════════
        // ADIM 2: Fingerprint bazlı akıllı diff (NON-DESTRUCTIVE)
        //         Kullanıcı etkileşimli kayıtlar ASLA silinmez.
        // ═══════════════════════════════════════════════════════════

        // Stopaj kayıtları özel: her analizde yeniden değerlendiriliyor
        // çünkü virman durumu değişebilir. Diğer kayıtlar diff ile yönetilir.
        var adayNonStopaj = adayKayitlar.Where(k => k.HesapTuru != BankaHesapTuru.Stopaj).ToList();
        var adayStopaj = adayKayitlar.Where(k => k.HesapTuru == BankaHesapTuru.Stopaj).ToList();

        // ─ Mevcut Acik kayıtlar (sadece bunlar silinebilir) ─
        // AnalizTarihi filtresi: sadece aynı güne ait Acik kayıtları diff'le
        var mevcutAcik = await _db.HesapKontrolKayitlari
            .Where(x => x.AnalizTarihi == analizTarihi
                     && x.Durum == KayitDurumu.Acik
                     && x.HesapTuru != BankaHesapTuru.Stopaj)
            .ToListAsync(ct);

        // ─ Tüm kullanıcı etkileşimli kayıtlar (duplicate kontrolü) ─
        // Takipte, Onaylandi, Cozuldu → kullanıcı bunları işlemiş, aynısı tekrar eklenmemeli.
        // AnalizTarihi filtresi: sadece aynı güne ait işlenmiş kayıtları kontrol et
        var kullaniciIslemliKayitlar = await _db.HesapKontrolKayitlari
            .Where(x => x.AnalizTarihi == analizTarihi
                      && x.Durum != KayitDurumu.Acik
                      && x.Durum != KayitDurumu.Iptal
                      && x.HesapTuru != BankaHesapTuru.Stopaj)
            .ToListAsync(ct);

        // Fingerprint bazlı eşleştirme (bire-bir, multiplicity korunur)
        var mevcutAcikPool = new List<HesapKontrolKaydi>(mevcutAcik);
        var islemliPool = new List<HesapKontrolKaydi>(kullaniciIslemliKayitlar);
        var eklenecek = new List<HesapKontrolKaydi>();

        foreach (var aday in adayNonStopaj)
        {
            var fp = GetRecordFingerprint(aday);

            // Önce kullanıcı etkileşimli kayıtlarda eşleşme ara
            // (Takipte/Onaylandi/Cozuldu — bu kayıt zaten işlenmiş, tekrar ekleme)
            var islemliMatch = islemliPool.FirstOrDefault(m => GetRecordFingerprint(m) == fp);
            if (islemliMatch != null)
            {
                islemliPool.Remove(islemliMatch); // 1:1 eşleşme — bir sonraki aynı fp başka kayıtla eşleşir
                continue; // Bu aday zaten işlenmiş, ekleme
            }

            // Sonra mevcut Acik kayıtlarda eşleşme ara
            // (Zaten Acik olarak var — tekrar silip eklemeye gerek yok)
            var acikMatch = mevcutAcikPool.FirstOrDefault(m => GetRecordFingerprint(m) == fp);
            if (acikMatch != null)
            {
                mevcutAcikPool.Remove(acikMatch); // Bu Acik kayıt korunacak
                continue; // Zaten mevcut
            }

            // Hiçbir eşleşme yok → gerçekten yeni kayıt
            eklenecek.Add(aday);
        }

        // mevcutAcikPool'da kalan kayıtlar artık tespit edilmiyor → eski/stale → silinecek
        var silinecek = mevcutAcikPool.ToList();

        // ─── Stopaj özel temizliği ───
        // Stopaj Acik kayıtları: her zaman yeniden oluştur
        var eskiStopajAcik = await _db.HesapKontrolKayitlari
            .Where(x => x.HesapTuru == BankaHesapTuru.Stopaj && x.Durum == KayitDurumu.Acik)
            .ToListAsync(ct);
        silinecek.AddRange(eskiStopajAcik);

        // Otomatik oluşturulmuş Stopaj Cozuldu kayıtları (duplikasyon önleme)
        var dupStopajKayitlar = await _db.HesapKontrolKayitlari
            .Where(x => x.HesapTuru == BankaHesapTuru.Stopaj
                     && x.Durum == KayitDurumu.Cozuldu
                     && string.IsNullOrEmpty(x.OnaylayanKullanici))
            .ToListAsync(ct);
        silinecek.AddRange(dupStopajKayitlar);
        eklenecek.AddRange(adayStopaj);

        // ═══════════════════════════════════════════════════════════
        // ADIM 3: DB Güncelleme
        // ═══════════════════════════════════════════════════════════

        if (silinecek.Count > 0)
        {
            _logger.LogInformation("Diff analiz: {Count} eski/stale kayıt siliniyor", silinecek.Count);
            _db.HesapKontrolKayitlari.RemoveRange(silinecek);
        }

        if (eklenecek.Count > 0)
        {
            _logger.LogInformation("Diff analiz: {Count} yeni kayıt ekleniyor", eklenecek.Count);
            _db.HesapKontrolKayitlari.AddRange(eklenecek);
        }

        if (silinecek.Count > 0 || eklenecek.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            _logger.LogInformation("Diff analiz: Değişiklik yok, mevcut kayıtlar korundu.");
        }

        // ─── CrossDay eşleştirme ───
        var crossDay = await CrossDayReconcileAsync(analizTarihi, ct);
        var kesinEslesme = crossDay.KesirEslesmeler;
        var potansiyelEslesme = crossDay.PotansiyelEslesmeler;

        // ─── Faz 2: Takipte süre aşımı kontrolü ───
        var takipteKayitlar = await _db.HesapKontrolKayitlari
            .Where(x => x.Durum == KayitDurumu.Takipte
                     && x.TakipBaslangicTarihi.HasValue)
            .ToListAsync(ct);

        var bugunDateOnly = DateOnly.FromDateTime(DateTime.Now);
        foreach (var kayit in takipteKayitlar)
        {
            var gun = bugunDateOnly.DayNumber - kayit.TakipBaslangicTarihi!.Value.DayNumber;
            // Günde 1'den fazla bildirim oluşturmayı engelle
            if (kayit.SonBildirimTarihi.HasValue
                && DateOnly.FromDateTime(kayit.SonBildirimTarihi.Value) >= bugunDateOnly)
                continue;

            string? uyari = null;
            if (gun >= 5)
                uyari = $"🔴 {gun} gündür gelmedi! Acil araştırma gerekli. ({kayit.Tutar:N2} ₺ {kayit.TespitEdilenTip ?? kayit.HesapTuru.ToString()})";
            else if (gun >= 2)
                uyari = $"⚠️ {gun} gündür gelmedi. Araştırmayı değerlendirin. ({kayit.Tutar:N2} ₺ {kayit.TespitEdilenTip ?? kayit.HesapTuru.ToString()})";

            if (uyari != null)
            {
                kayit.Notlar = (kayit.Notlar ?? "") + $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] {uyari}";
                kayit.SonBildirimTarihi = DateTime.UtcNow;
            }
        }

        if (takipteKayitlar.Any(x => x.SonBildirimTarihi.HasValue))
            await _db.SaveChangesAsync(ct);

        // ─── Özet oluştur ───
        var fazla = adayKayitlar.Count(x => x.Yon == KayitYonu.Fazla);
        var eksik = adayKayitlar.Count(x => x.Yon == KayitYonu.Eksik);
        var netTahsilat = adayKayitlar
            .Where(x => x.HesapTuru == BankaHesapTuru.Tahsilat)
            .Sum(x => x.Yon == KayitYonu.Fazla ? x.Tutar : -x.Tutar);
        var netHarc = adayKayitlar
            .Where(x => x.HesapTuru == BankaHesapTuru.Harc)
            .Sum(x => x.Yon == KayitYonu.Fazla ? x.Tutar : -x.Tutar);

        var ozetMesaj = $"Toplam {adayKayitlar.Count} kayıt tespit edildi " +
                        $"(+{eklenecek.Count} yeni, -{silinecek.Count} stale). " +
                        $"Fazla: {fazla}, Eksik: {eksik}. " +
                        (kesinEslesme.Count > 0 ? $"CrossDay: {kesinEslesme.Count} kesin eşleşme. " : "") +
                        (potansiyelEslesme.Count > 0 ? $"\u26a0\ufe0f {potansiyelEslesme.Count} kısmi eşleşme (onay bekliyor)." : "");

        _logger.LogInformation("HesapKontrol analiz tamamlandı: {Ozet}", ozetMesaj);

        return new HesapKontrolRapor(
            analizTarihi,
            adayKayitlar.Count,
            fazla,
            eksik,
            netTahsilat,
            netHarc,
            stopajDurum,
            kesinEslesme,
            potansiyelEslesme,
            ozetMesaj);
    }

    // ═════════════════════════════════════════════════════════════
    // B3: CrossDayReconcile
    // ═════════════════════════════════════════════════════════════

    public async Task<CrossDayResult> CrossDayReconcileAsync(
        DateOnly bugunTarihi,
        CancellationToken ct = default)
    {
        var eslesmeler = new List<CrossDayMatch>();

        // ─── Adım 0: Yetim (Orphan) kayıtları tespit et ve yeniden aç ───
        // Cozuldu durumundaki kayıtların CozulmeKaynakId'si hâlâ geçerli mi kontrol et.
        // Kullanıcı verileri silip yeniden oluşturduğunda eşleşen kayıtlar kaybolabilir.
        var cozulduKayitlar = await _db.HesapKontrolKayitlari
            .Where(x => x.Durum == KayitDurumu.Cozuldu
                     && x.CozulmeKaynakId.HasValue
                     && string.IsNullOrEmpty(x.OnaylayanKullanici)) // Kullanıcı onayı yok → otomatik çözülmüş
            .ToListAsync(ct);

        if (cozulduKayitlar.Count > 0)
        {
            var kaynakIdler = cozulduKayitlar.Select(x => x.CozulmeKaynakId!.Value).Distinct().ToList();
            var mevcutKaynaklar = await _db.HesapKontrolKayitlari
                .Where(x => kaynakIdler.Contains(x.Id))
                .Select(x => x.Id)
                .ToListAsync(ct);
            var mevcutKaynakSet = new HashSet<Guid>(mevcutKaynaklar);

            var yetimler = cozulduKayitlar
                .Where(x => !mevcutKaynakSet.Contains(x.CozulmeKaynakId!.Value))
                .ToList();

            foreach (var yetim in yetimler)
            {
                _logger.LogInformation(
                    "CrossDay orphan: {Id} ({HesapTuru} {Yon} {Tutar:N2}) yetim — kaynak {Kaynak} mevcut değil, Açık'a döndürülüyor",
                    yetim.Id, yetim.HesapTuru, yetim.Yon, yetim.Tutar, yetim.CozulmeKaynakId);
                yetim.Durum = KayitDurumu.Acik;
                yetim.CozulmeTarihi = null;
                yetim.CozulmeKaynakId = null;
                yetim.Notlar = (yetim.Notlar ?? "") +
                    $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] ↩ Otomatik yeniden açıldı (eşleşen kayıt silindi, yetim kaldı)";
            }

            if (yetimler.Count > 0)
            {
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("CrossDay: {Count} yetim kayıt Açık'a döndürüldü", yetimler.Count);
            }
        }

        // ─── Adım 1: Eşleştirilecek Eksik kayıtları bul ───
        // Takipte kayıtlar: kullanıcı tarafından onaylanmış gerçek eksikler → tarih kısıtı YOK
        // Açık kayıtlar: yalnızca önceki günlerin (bugunTarihi'nden önceki) kayıtları
        // ÖNCELİK: Takipte (4) → Acik (0) sıralaması — kullanıcı onaylı kayıtlar önce eşleşir
        var acikEksikler = await _db.HesapKontrolKayitlari
            .Where(x => (x.Durum == KayitDurumu.Acik || x.Durum == KayitDurumu.Takipte)
                     && x.Yon == KayitYonu.Eksik
                     && (x.Durum == KayitDurumu.Takipte || x.AnalizTarihi < bugunTarihi))
            .OrderByDescending(x => x.Durum) // Takipte=4 önce, Acik=0 sonra
            .ToListAsync(ct);

        // ─── Adım 2: Eşleştirilecek Fazla kayıtları bul ───
        var bugunFazlalar = await _db.HesapKontrolKayitlari
            .Where(x => x.AnalizTarihi <= bugunTarihi
                     && (x.Durum == KayitDurumu.Acik || x.Durum == KayitDurumu.Takipte)
                     && x.Yon == KayitYonu.Fazla)
            .ToListAsync(ct);

        // Tutar toleransı: ±0.01₺ (kuruş yuvarlama farkları)
        const decimal tutarTolerans = 0.01m;
        var kesinEslesmeler = new List<CrossDayMatch>();
        var potansiyelEslesmeler = new List<CrossDayMatch>();

        foreach (var eksik in acikEksikler)
        {
            // BİREBİR tutar eşleşmesi (toleranslı)
            var eslesenFazla = bugunFazlalar
                .FirstOrDefault(f => f.HesapTuru == eksik.HesapTuru
                                  && Math.Abs(f.Tutar - eksik.Tutar) <= tutarTolerans
                                  && (f.Durum == KayitDurumu.Acik || f.Durum == KayitDurumu.Takipte));

            if (eslesenFazla == null) continue;

            // ─── Güven seviyesi belirleme ───
            // DosyaNo varsa → Fazla'nın Aciklama metninde geçiyor mu kontrol et
            var guven = CrossDayGuven.Kismi; // Varsayılan: kısmi (sadece tutar eşleşmesi)

            if (!string.IsNullOrEmpty(eksik.DosyaNo) && !string.IsNullOrEmpty(eslesenFazla.Aciklama))
            {
                // DosyaNo banka açıklamasında geçiyor mu? (ör: "#2025/763 Ankara 8. İdare Mahkemesi")
                if (eslesenFazla.Aciklama.Contains(eksik.DosyaNo, StringComparison.OrdinalIgnoreCase))
                    guven = CrossDayGuven.Tam;
            }
            else if (string.IsNullOrEmpty(eksik.DosyaNo))
            {
                // DosyaNo yok → ek doğrulama yapılamaz, TespitEdilenTip ile destekle
                // Aynı tip ise (ör: ikisi de BEDELI_GELMEMIS) kısmi kalır
                guven = CrossDayGuven.Kismi;
            }

            var match = new CrossDayMatch(
                eksik.Id, eslesenFazla.Id,
                eksik.Tutar, eksik.HesapTuru,
                eksik.AnalizTarihi, bugunTarihi,
                guven,
                eksik.DosyaNo, eksik.BirimAdi,
                eslesenFazla.Aciklama);

            if (guven == CrossDayGuven.Tam)
            {
                // ─── TAM GÜVEN: Otomatik çöz ───
                var gun = (bugunTarihi.DayNumber - eksik.AnalizTarihi.DayNumber);
                var bildirimNotu = gun <= 1
                    ? $"✅ Dünkü eksik kayıt ({eksik.DosyaNo ?? ""} {eksik.Tutar:N2} ₺) bugün geldi — DosyaNo doğrulandı ✓"
                    : $"✅ {gun} gün önceki eksik kayıt ({eksik.DosyaNo ?? ""} {eksik.Tutar:N2} ₺) bugün geldi — DosyaNo doğrulandı ✓";

                eksik.Durum = KayitDurumu.Cozuldu;
                eksik.CozulmeTarihi = bugunTarihi;
                eksik.CozulmeKaynakId = eslesenFazla.Id;
                eksik.Notlar = (eksik.Notlar ?? "") +
                    $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] {bildirimNotu} — Eşleşen fazla: {eslesenFazla.Id:N}";

                eslesenFazla.Durum = KayitDurumu.Cozuldu;
                eslesenFazla.CozulmeTarihi = bugunTarihi;
                eslesenFazla.CozulmeKaynakId = eksik.Id;
                eslesenFazla.Notlar = (eslesenFazla.Notlar ?? "") +
                    $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] {bildirimNotu} — Eşleşen eksik: {eksik.Id:N}";

                bugunFazlalar.Remove(eslesenFazla);
                kesinEslesmeler.Add(match);
            }
            else
            {
                // ─── KISMİ GÜVEN: Otomatik çözme, potansiyel olarak işaretle ───
                _logger.LogInformation(
                    "CrossDay kısmi eşleşme: Eksik {EksikId} ({DosyaNo}, {Tutar:N2}) ↔ Fazla {FazlaId} — DosyaNo doğrulanamadı",
                    eksik.Id, eksik.DosyaNo, eksik.Tutar, eslesenFazla.Id);

                // Fazla'yı listeden çıkar (1:1 — başka kısmi eşleşmeyle çakışmasın)
                bugunFazlalar.Remove(eslesenFazla);
                potansiyelEslesmeler.Add(match);
            }
        }

        if (kesinEslesmeler.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("CrossDay: {Kesin} kesin, {Potansiyel} potansiyel eşleşme",
                kesinEslesmeler.Count, potansiyelEslesmeler.Count);
        }

        return new CrossDayResult(kesinEslesmeler, potansiyelEslesmeler);
    }

    // ═════════════════════════════════════════════════════════════
    // B4: CheckStopajVirman
    // ═════════════════════════════════════════════════════════════

    public async Task<StopajVirmanDurum> CheckStopajVirmanAsync(
        DateOnly tarihi,
        decimal toplamStopaj,
        string uploadFolder,
        CancellationToken ct = default)
    {
        if (toplamStopaj <= 0)
            return new StopajVirmanDurum(true, 0, null, "Stopaj tutarı yok.");

        try
        {
            // Reddiyat karşılaştırmasını kullanarak virman kontrol
            var reddiyatResult = await _comparison.CompareReddiyatCikisAsync(uploadFolder, ct: ct);
            if (reddiyatResult.Ok && reddiyatResult.Value != null)
                return CheckStopajFromReport(reddiyatResult.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stopaj virman kontrolü sırasında hata");
        }

        return new StopajVirmanDurum(
            false, toplamStopaj, null,
            $"⚠️ Stopaj Hesabına {toplamStopaj:N2}₺ Virman yapılıp yapılmadığı kontrol edilemedi.");
    }
}
