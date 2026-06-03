#nullable enable
using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<DateOnly, SemaphoreSlim> _crossDayLocks = new();

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

        var stopajDurum = new StopajVirmanDurum(false, 0, null, "Reddiyat verisi yok", StopajStatus.Error);
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

                stopajDurum = CheckStopajFromAllVirmans(toplamStopaj, tumVirmanlar, reddiyatResult.Value.CancelledPairs);

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

        // 90 günlük dedup penceresi — hem Acik hem İşlenmiş kayıtlar için ortak
        var dedupWindowStart = analizTarihi.AddDays(-90);

        // ─ Mevcut Acik kayıtlar — 90 günlük sliding window ─
        // PATCH 3 FIX: Kümülatif banka dosyalarında dünün açık kaydı bugünün
        // dosyasında tekrar gelince, eski sadece-bugün sorgusu geçmiş açık kaydı
        // göremiyordu → aynı işlem her gün yeni satır olarak insert ediliyordu.
        // Artık kullaniciIslemliKayitlar ile aynı 90 günlük pencere kullanılır.
        // Multiplicity: Pool Remove() ile 1:1 tüketim — gerçekten aynı gün gelen
        // iki ayrı 470₺ EFT varsa ikincisi yeni kayıt olarak doğru şekilde eklenir.
        var mevcutAcik = await _db.HesapKontrolKayitlari
            .Where(x => x.AnalizTarihi >= dedupWindowStart
                     && x.AnalizTarihi <= analizTarihi
                     && x.Durum == KayitDurumu.Acik
                     && x.HesapTuru != BankaHesapTuru.Stopaj)
            .ToListAsync(ct);

        // ─ Kullanıcı etkileşimli kayıtlar — 90 günlük sliding window ─
        // PATCH 2: Eski kod sadece AnalizTarihi == analizTarihi ile aynı günün
        // işlenmiş kayıtlarını çekiyordu. Kümülatif banka dosyasında dün Yoksay
        // (İptal) edilen kayıt bugün tekrar gelince yeni Açık kayıt olarak
        // üretiliyordu. Şimdi 90 günlük sliding window ile geçmiş kararlar
        // hatırlanır. Eşleştirme GetFollowIdentityFingerprint (tarih bağımsız)
        // ile yapılır — farklı günlerdeki aynı banka hareketi doğru eşleşir.
        var kullaniciIslemliKayitlar = await _db.HesapKontrolKayitlari
            .Where(x => x.AnalizTarihi >= dedupWindowStart
                      && x.AnalizTarihi <= analizTarihi
                      && x.Durum != KayitDurumu.Acik
                      && x.Durum != KayitDurumu.Takipte // Takipte ayrı pool'da yönetilir
                      && x.HesapTuru != BankaHesapTuru.Stopaj)
            .ToListAsync(ct);

        _logger.LogInformation(
            "[HK-DEDUP-MEMORY] Date={Date} WindowStart={WindowStart} ProcessedCount={ProcessedCount} " +
            "IgnoredCount={IgnoredCount} ResolvedCount={ResolvedCount}",
            analizTarihi, dedupWindowStart, kullaniciIslemliKayitlar.Count,
            kullaniciIslemliKayitlar.Count(x => x.Durum == KayitDurumu.Iptal),
            kullaniciIslemliKayitlar.Count(x => x.Durum == KayitDurumu.Cozuldu));

        var takipteAyniGercekKayitlar = await _db.HesapKontrolKayitlari
            .Where(x => x.Durum == KayitDurumu.Takipte
                     && x.HesapTuru != BankaHesapTuru.Stopaj)
            .ToListAsync(ct);

        // Fingerprint bazlı eşleştirme (bire-bir, multiplicity korunur)
        var mevcutAcikPool = new List<HesapKontrolKaydi>(mevcutAcik);
        var islemliPool = new List<HesapKontrolKaydi>(kullaniciIslemliKayitlar);
        var takiptePool = new List<HesapKontrolKaydi>(takipteAyniGercekKayitlar);
        var eklenecek = new List<HesapKontrolKaydi>();
        var pasiflestirilecek = new List<HesapKontrolKaydi>();
        var takipteMergeDegisti = false;
        var skippedByMemory = 0;

        foreach (var aday in adayNonStopaj)
        {
            var fp = GetRecordFingerprint(aday);
            var followFp = GetFollowIdentityFingerprint(aday);
            _logger.LogInformation(
                "[HK-FOLLOW-FINGERPRINT] Date={Date} Fingerprint={Fingerprint} FollowIdentity={FollowIdentity} HesapTuru={HesapTuru} Yon={Yon} DosyaNo={DosyaNo} Birim={Birim} Tutar={Tutar}",
                aday.AnalizTarihi,
                fp,
                followFp,
                aday.HesapTuru,
                aday.Yon,
                aday.DosyaNo,
                aday.BirimAdi,
                aday.Tutar);

            var takipteMatch = takiptePool.FirstOrDefault(m => GetFollowIdentityFingerprint(m) == followFp);
            if (takipteMatch != null)
            {
                takiptePool.Remove(takipteMatch);
                var eskiTarih = takipteMatch.AnalizTarihi;
                var orijinalTarih = takipteMatch.AnalizTarihi <= aday.AnalizTarihi
                    ? takipteMatch.AnalizTarihi
                    : aday.AnalizTarihi;
                var mergeChanged = false;

                if (takipteMatch.AnalizTarihi != orijinalTarih)
                {
                    takipteMatch.AnalizTarihi = orijinalTarih;
                    mergeChanged = true;
                }

                var acikDuplicate = mevcutAcikPool.FirstOrDefault(m => GetFollowIdentityFingerprint(m) == followFp);
                if (acikDuplicate != null)
                {
                    mevcutAcikPool.Remove(acikDuplicate);
                    acikDuplicate.Durum = KayitDurumu.Iptal;
                    acikDuplicate.CozulmeTarihi = analizTarihi;
                    acikDuplicate.Notlar = (acikDuplicate.Notlar ?? "") +
                        $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] Duplicate takip kaydi ile birlestirildi. Aktif takip: {takipteMatch.Id:N}";
                    pasiflestirilecek.Add(acikDuplicate);
                    mergeChanged = true;
                }

                if (mergeChanged)
                {
                    takipteMatch.Notlar = (takipteMatch.Notlar ?? "") +
                        $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] Orijinal eksik tarihi {orijinalTarih:dd.MM.yyyy} olarak hizalandi. Fingerprint: {followFp}";
                    takipteMergeDegisti = true;

                    _logger.LogInformation(
                        "[HK-DUPLICATE-FOLLOW-MERGED] FollowIdentity={FollowIdentity} TrackedId={TrackedId} OldTrackedDate={OldTrackedDate} NewTrackedDate={NewTrackedDate} OpenDuplicateId={OpenDuplicateId} OpenDuplicateStatus={OpenDuplicateStatus}",
                        followFp,
                        takipteMatch.Id,
                        eskiTarih,
                        takipteMatch.AnalizTarihi,
                        acikDuplicate?.Id,
                        acikDuplicate?.Durum);
                }

                continue;
            }

            // Önce kullanıcı etkileşimli kayıtlarda eşleşme ara (90 gün hafıza)
            // PATCH 2 FIX: GetFollowIdentityFingerprint (tarih bağımsız) kullan.
            // Eski: GetRecordFingerprint → tarih dahildi → farklı günün yoksayılmış
            // kaydı eşleşmiyordu. Yeni: followFp ile tarih bağımsız eşleştirme.
            var islemliMatch = islemliPool.FirstOrDefault(m => GetFollowIdentityFingerprint(m) == followFp);
            if (islemliMatch != null)
            {
                islemliPool.Remove(islemliMatch); // 1:1 eşleşme — bir sonraki aynı fp başka kayıtla eşleşir
                skippedByMemory++;
                continue; // Bu aday zaten işlenmiş (Yoksay/Çözüldü/İptal), tekrar ekleme
            }

            // Sonra mevcut Acik kayıtlarda eşleşme ara (tarih bağımsız)
            // PATCH 3 FIX: GetRecordFingerprint tarih içerdiği için geçmiş günün
            // açık kaydı eşleşmiyordu → mükerrer insert. Artık followFp ile
            // tarih bağımsız eşleştirme yapılır; eski kayıt orijinal tarihiyle korunur.
            // OrderBy(AnalizTarihi): Aynı fingerprint'ten birden fazla açık kayıt
            // varsa EN ESKİ'yi korur — bugüne ait stale duplicate pool'da kalır
            // ve stale cleanup tarafından silinir.
            var acikMatch = mevcutAcikPool
                .OrderBy(m => m.AnalizTarihi)
                .FirstOrDefault(m => GetFollowIdentityFingerprint(m) == followFp);
            if (acikMatch != null)
            {
                mevcutAcikPool.Remove(acikMatch); // Bu Acik kayıt korunacak
                continue; // Zaten mevcut
            }

            // Hiçbir eşleşme yok → gerçekten yeni kayıt
            eklenecek.Add(aday);
        }

        _logger.LogInformation(
            "[HK-DEDUP-MEMORY] Date={Date} SkippedByMemory={SkippedByMemory} NewRecords={NewRecords}",
            analizTarihi, skippedByMemory, eklenecek.Count);

        // mevcutAcikPool'da kalan kayıtlar: sadece AYNI güne ait stale kayıtları sil
        // Farklı günlere ait Acik kayıtlara dokunma (onlar kendi günlerinde yönetilir)
        var silinecek = mevcutAcikPool
            .Where(x => x.AnalizTarihi == analizTarihi)
            .ToList();

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
        // ADIM 3: DB Güncelleme (Transaction ile atomik)
        // ═══════════════════════════════════════════════════════════

        if (silinecek.Count > 0 || eklenecek.Count > 0 || pasiflestirilecek.Count > 0 || takipteMergeDegisti)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);

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

                if (pasiflestirilecek.Count > 0)
                {
                    _logger.LogInformation("[HK-DUPLICATE-FOLLOW-MERGED] PassiveDuplicates={Count}", pasiflestirilecek.Count);
                }

                if (takipteMergeDegisti)
                {
                    _logger.LogInformation("[HK-DUPLICATE-FOLLOW-MERGED] TrackingDateRealigned=true");
                }
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
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
        var semaphore = _crossDayLocks.GetOrAdd(bugunTarihi, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        try
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
        // EN ESKİ İLK: Aynı Durum grubunda en eski kayıt önce eşleşir (FIFO)
        var acikEksikler = await _db.HesapKontrolKayitlari
            .Where(x => (x.Durum == KayitDurumu.Acik || x.Durum == KayitDurumu.Takipte)
                     && x.Yon == KayitYonu.Eksik
                     && (x.Durum == KayitDurumu.Takipte || x.AnalizTarihi < bugunTarihi))
            .OrderByDescending(x => x.Durum) // Takipte=4 önce, Acik=0 sonra
            .ThenBy(x => x.AnalizTarihi)     // ← En eski kayıt önce eşleşir (FIFO)
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
            // BİREBİR tutar eşleşmesi — DosyaNo/BirimAdi ile doğrulanmış Fazla öncelikli
            var tutarEslesenler = bugunFazlalar
                .Where(f => f.HesapTuru == eksik.HesapTuru
                         && Math.Abs(f.Tutar - eksik.Tutar) <= tutarTolerans
                         && (f.Durum == KayitDurumu.Acik || f.Durum == KayitDurumu.Takipte))
                .ToList();

            if (tutarEslesenler.Count == 0) continue;

            // Fazla seçim önceliği: DosyaNo eşleşmesi > BirimAdi eşleşmesi > sadece tutar
            var eslesenFazla = tutarEslesenler
                .OrderByDescending(f =>
                    !string.IsNullOrEmpty(eksik.DosyaNo)
                    && !string.IsNullOrEmpty(f.Aciklama)
                    && f.Aciklama.Contains(eksik.DosyaNo, StringComparison.OrdinalIgnoreCase) ? 2 :
                    !string.IsNullOrEmpty(eksik.BirimAdi)
                    && !string.IsNullOrEmpty(f.Aciklama)
                    && f.Aciklama.Contains(eksik.BirimAdi, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .First();

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

        // ─── Adım 4: Dedup Cleanup — Çözülen DosyaNo'ların eski kayıtlarını da çöz ───
        // Aynı DosyaNo+HesapTuru+Tutar'a sahip eski Acik kayıtlar,
        // çözülen yeni kayıtla aynı gerçek dünya işlemini temsil eder.
        if (kesinEslesmeler.Count > 0)
        {
            var cozulenDosyaNolar = kesinEslesmeler
                .Where(m => !string.IsNullOrEmpty(m.EksikDosyaNo))
                .Select(m => new { m.EksikDosyaNo, m.HesapTuru, m.Tutar })
                .ToList();

            if (cozulenDosyaNolar.Count > 0)
            {
                var kalanAciklar = await _db.HesapKontrolKayitlari
                    .Where(x => x.Durum == KayitDurumu.Acik
                             && x.Yon == KayitYonu.Eksik
                             && x.AnalizTarihi < bugunTarihi)
                    .ToListAsync(ct);

                var dedupSayisi = 0;
                foreach (var kalan in kalanAciklar)
                {
                    var eslesen = cozulenDosyaNolar
                        .FirstOrDefault(c => c.EksikDosyaNo == kalan.DosyaNo
                                          && c.HesapTuru == kalan.HesapTuru
                                          && Math.Abs(c.Tutar - kalan.Tutar) <= tutarTolerans);
                    if (eslesen != null)
                    {
                        kalan.Durum = KayitDurumu.Cozuldu;
                        kalan.CozulmeTarihi = bugunTarihi;
                        kalan.Notlar = (kalan.Notlar ?? "") +
                            $"\n[{DateTime.UtcNow:dd.MM.yyyy HH:mm}] ✅ Aynı DosyaNo ({kalan.DosyaNo}) için " +
                            $"daha yeni kayıt çözüldü — bu eski kayıt da otomatik kapatıldı.";
                        dedupSayisi++;
                    }
                }

                if (dedupSayisi > 0)
                    _logger.LogInformation("CrossDay dedup: {Count} eski duplicate kayıt otomatik kapatıldı", dedupSayisi);
            }
        }

        if (kesinEslesmeler.Count > 0)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
            _logger.LogInformation("CrossDay: {Kesin} kesin, {Potansiyel} potansiyel eşleşme",
                kesinEslesmeler.Count, potansiyelEslesmeler.Count);
        }

            return new CrossDayResult(kesinEslesmeler, potansiyelEslesmeler);
        }
        finally
        {
            semaphore.Release();
        }
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
            return new StopajVirmanDurum(true, 0, null, "Stopaj tutarı yok.", StopajStatus.Ok);

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
            $"⚠️ Stopaj Hesabına {toplamStopaj:N2}₺ Virman yapılıp yapılmadığı kontrol edilemedi.",
            StopajStatus.Error);
    }
}
