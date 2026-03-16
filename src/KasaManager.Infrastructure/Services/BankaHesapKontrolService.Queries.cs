#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// MS2 CQRS-lite: Okuma işlemleri (Get*, Dashboard, AutoFill).
/// </summary>
public sealed partial class BankaHesapKontrolService
{
    // ═════════════════════════════════════════════════════════════
    // Sorgulama
    // ═════════════════════════════════════════════════════════════

    public async Task<List<HesapKontrolKaydi>> GetOpenItemsAsync(
        BankaHesapTuru? hesapTuru = null,
        DateOnly? baslangic = null,
        DateOnly? bitis = null,
        CancellationToken ct = default)
    {
        var query = _db.HesapKontrolKayitlari
            .Where(x => x.Durum == KayitDurumu.Acik);

        if (hesapTuru.HasValue)
            query = query.Where(x => x.HesapTuru == hesapTuru.Value);
        if (baslangic.HasValue)
            query = query.Where(x => x.AnalizTarihi >= baslangic.Value);
        if (bitis.HasValue)
            query = query.Where(x => x.AnalizTarihi <= bitis.Value);

        return await query
            .OrderByDescending(x => x.AnalizTarihi)
            .ThenBy(x => x.HesapTuru)
            .ToListAsync(ct);
    }

    public async Task<List<HesapKontrolKaydi>> GetTrackedItemsAsync(
        BankaHesapTuru? hesapTuru = null,
        CancellationToken ct = default)
    {
        var query = _db.HesapKontrolKayitlari
            .Where(x => x.Durum == KayitDurumu.Takipte);

        if (hesapTuru.HasValue)
            query = query.Where(x => x.HesapTuru == hesapTuru.Value);

        return await query
            .OrderByDescending(x => x.AnalizTarihi)
            .ThenBy(x => x.HesapTuru)
            .ToListAsync(ct);
    }

    public async Task<List<HesapKontrolKaydi>> GetHistoryAsync(
        DateOnly baslangic,
        DateOnly bitis,
        BankaHesapTuru? hesapTuru = null,
        KayitDurumu? durum = null,
        CancellationToken ct = default)
    {
        var query = _db.HesapKontrolKayitlari
            .Where(x => x.AnalizTarihi >= baslangic && x.AnalizTarihi <= bitis);

        if (hesapTuru.HasValue)
            query = query.Where(x => x.HesapTuru == hesapTuru.Value);
        if (durum.HasValue)
            query = query.Where(x => x.Durum == durum.Value);

        return await query
            .OrderByDescending(x => x.AnalizTarihi)
            .ThenBy(x => x.HesapTuru)
            .ToListAsync(ct);
    }

    public async Task<List<HesapKontrolKaydi>> GetTrackingLifecycleAsync(
        DateOnly baslangic,
        DateOnly bitis,
        BankaHesapTuru? hesapTuru = null,
        KayitDurumu? durum = null,
        CancellationToken ct = default)
    {
        var query = _db.HesapKontrolKayitlari
            .Where(x => x.TakipBaslangicTarihi != null
                     && x.TakipBaslangicTarihi >= baslangic
                     && x.TakipBaslangicTarihi <= bitis);

        if (hesapTuru.HasValue)
            query = query.Where(x => x.HesapTuru == hesapTuru.Value);
        if (durum.HasValue)
            query = query.Where(x => x.Durum == durum.Value);

        return await query
            .OrderByDescending(x => x.TakipBaslangicTarihi)
            .ThenBy(x => x.HesapTuru)
            .ThenBy(x => x.Yon)
            .ToListAsync(ct);
    }

    public async Task<TakipOzeti> GetTrackingSummaryAsync(CancellationToken ct = default)
    {
        var bugun = DateOnly.FromDateTime(DateTime.Now);

        var aktifTakip = await _db.HesapKontrolKayitlari
            .Where(x => x.Durum == KayitDurumu.Takipte && x.TakipBaslangicTarihi.HasValue)
            .ToListAsync(ct);

        var bugunCozulenler = await _db.HesapKontrolKayitlari
            .Where(x => x.CozulmeTarihi == bugun
                     && x.TakipBaslangicTarihi.HasValue
                     && (x.Durum == KayitDurumu.Cozuldu || x.Durum == KayitDurumu.Onaylandi))
            .ToListAsync(ct);

        var toplamEksik = aktifTakip.Where(x => x.Yon == KayitYonu.Eksik).Sum(x => x.Tutar);
        var toplamFazla = aktifTakip.Where(x => x.Yon == KayitYonu.Fazla).Sum(x => x.Tutar);

        var gunler = aktifTakip
            .Select(x => bugun.DayNumber - x.TakipBaslangicTarihi!.Value.DayNumber)
            .ToList();
        var ortalamaGun = gunler.Count > 0 ? gunler.Average() : 0;
        var enEskiGun = gunler.Count > 0 ? gunler.Max() : 0;

        var gunBazli = aktifTakip
            .GroupBy(x => bugun.DayNumber - x.TakipBaslangicTarihi!.Value.DayNumber)
            .Select(g => new GunBazliTakip(
                g.Key,
                g.Count(),
                g.Sum(x => x.Tutar),
                g.Key >= 5 ? "kritik" : g.Key >= 2 ? "uyari" : "normal"))
            .OrderBy(x => x.GunSayisi)
            .ToList();

        var bugunCozulenToplam = bugunCozulenler.Sum(x => x.Tutar);

        return new TakipOzeti(
            aktifTakip.Count,
            toplamEksik,
            toplamFazla,
            ortalamaGun,
            enEskiGun,
            bugunCozulenler,
            bugunCozulenToplam,
            gunBazli);
    }

    public async Task<HesapKontrolDashboard> GetDashboardAsync(DateOnly? analizTarihi = null, CancellationToken ct = default)
    {
        var acikQuery = _db.HesapKontrolKayitlari
            .Where(x => x.Durum == KayitDurumu.Acik);
        if (analizTarihi.HasValue)
            acikQuery = acikQuery.Where(x => x.AnalizTarihi == analizTarihi.Value);
        var acikKayitlar = await acikQuery.ToListAsync(ct);

        var takipteKayitlar = await _db.HesapKontrolKayitlari
            .Where(x => x.Durum == KayitDurumu.Takipte)
            .ToListAsync(ct);

        var bugun = DateOnly.FromDateTime(DateTime.UtcNow);
        var bugunCozulen = await _db.HesapKontrolKayitlari
            .CountAsync(x => x.CozulmeTarihi == bugun
                          && (x.Durum == KayitDurumu.Cozuldu || x.Durum == KayitDurumu.Onaylandi), ct);

        var sonStopaj = await _db.HesapKontrolKayitlari
            .Where(x => x.HesapTuru == BankaHesapTuru.Stopaj)
            .OrderByDescending(x => x.AnalizTarihi)
            .ThenByDescending(x => x.OlusturmaTarihi)
            .FirstOrDefaultAsync(ct);

        StopajVirmanDurum? stopajDurum = null;
        if (sonStopaj != null)
        {
            stopajDurum = new StopajVirmanDurum(
                sonStopaj.TespitEdilenTip == "STOPAJ_VIRMAN_OK",
                sonStopaj.Tutar,
                sonStopaj.TespitEdilenTip == "STOPAJ_VIRMAN_OK" ? sonStopaj.Tutar : null,
                sonStopaj.Aciklama ?? "Stopaj bilgisi mevcut.");
        }

        return new HesapKontrolDashboard(
            acikKayitlar.Count,
            acikKayitlar.Count(x => x.Sinif == FarkSinifi.Beklenen),
            acikKayitlar.Count(x => x.Sinif == FarkSinifi.Bilinmeyen),
            acikKayitlar.Where(x => x.Yon == KayitYonu.Eksik).Sum(x => x.Tutar),
            acikKayitlar.Where(x => x.Yon == KayitYonu.Fazla).Sum(x => x.Tutar),
            bugunCozulen,
            takipteKayitlar.Count,
            takipteKayitlar.Where(x => x.Yon == KayitYonu.Eksik).Sum(x => x.Tutar),
            takipteKayitlar.Where(x => x.Yon == KayitYonu.Fazla).Sum(x => x.Tutar),
            stopajDurum);
    }

    // ═════════════════════════════════════════════════════════════
    // Faz 3: Tarih Bazlı Tam Dashboard Sorgulaması ("Zaman Makinesi")
    // ═════════════════════════════════════════════════════════════

    public async Task<HesapKontrolDateSnapshot> GetDashboardForDateAsync(
        DateOnly tarih,
        CancellationToken ct = default)
    {
        var tumKayitlar = await _db.HesapKontrolKayitlari
            .Where(x => x.AnalizTarihi == tarih)
            .OrderBy(x => x.HesapTuru)
            .ThenBy(x => x.Yon)
            .ToListAsync(ct);

        var acik = tumKayitlar.Where(x => x.Durum == KayitDurumu.Acik).ToList();
        var takipte = tumKayitlar.Where(x => x.Durum == KayitDurumu.Takipte).ToList();
        var onaylanan = tumKayitlar.Where(x => x.Durum == KayitDurumu.Onaylandi).ToList();
        var cozulen = tumKayitlar.Where(x => x.Durum == KayitDurumu.Cozuldu).ToList();
        var iptal = tumKayitlar.Where(x => x.Durum == KayitDurumu.Iptal).ToList();

        var oGunCozulen = await _db.HesapKontrolKayitlari
            .CountAsync(x => x.CozulmeTarihi == tarih
                          && (x.Durum == KayitDurumu.Cozuldu || x.Durum == KayitDurumu.Onaylandi), ct);

        var sonStopaj = tumKayitlar
            .Where(x => x.HesapTuru == BankaHesapTuru.Stopaj)
            .OrderByDescending(x => x.OlusturmaTarihi)
            .FirstOrDefault();

        StopajVirmanDurum? stopajDurum = null;
        if (sonStopaj != null)
        {
            stopajDurum = new StopajVirmanDurum(
                sonStopaj.TespitEdilenTip == "STOPAJ_VIRMAN_OK",
                sonStopaj.Tutar,
                sonStopaj.TespitEdilenTip == "STOPAJ_VIRMAN_OK" ? sonStopaj.Tutar : null,
                sonStopaj.Aciklama ?? "Stopaj bilgisi mevcut.");
        }

        var dashboard = new HesapKontrolDashboard(
            acik.Count,
            acik.Count(x => x.Sinif == FarkSinifi.Beklenen),
            acik.Count(x => x.Sinif == FarkSinifi.Bilinmeyen),
            acik.Where(x => x.Yon == KayitYonu.Eksik).Sum(x => x.Tutar),
            acik.Where(x => x.Yon == KayitYonu.Fazla).Sum(x => x.Tutar),
            oGunCozulen,
            takipte.Count,
            takipte.Where(x => x.Yon == KayitYonu.Eksik).Sum(x => x.Tutar),
            takipte.Where(x => x.Yon == KayitYonu.Fazla).Sum(x => x.Tutar),
            stopajDurum);

        var autoFill = await GetAutoFillDataAsync(tarih, ct);

        var mesaj = tumKayitlar.Count > 0
            ? $"📊 {tarih:dd.MM.yyyy} tarihine ait {tumKayitlar.Count} kayıt bulundu. " +
              $"(Açık: {acik.Count}, Takipte: {takipte.Count}, Çözülmüş: {cozulen.Count + onaylanan.Count}, İptal: {iptal.Count})"
            : $"ℹ️ {tarih:dd.MM.yyyy} tarihine ait kayıt bulunamadı.";

        return new HesapKontrolDateSnapshot(
            tarih,
            dashboard,
            acik,
            takipte,
            onaylanan,
            cozulen,
            iptal,
            autoFill,
            mesaj);
    }

    // ═════════════════════════════════════════════════════════════
    // B6: Auto-Fill (Sabah Kasa Textbox Doldurma)
    // ═════════════════════════════════════════════════════════════

    public async Task<EksikFazlaAutoFill> GetAutoFillDataAsync(
        DateOnly analizTarihi,
        CancellationToken ct = default)
    {
        var bugunKayitlar = await _db.HesapKontrolKayitlari
            .Where(x => x.AnalizTarihi == analizTarihi)
            .ToListAsync(ct);

        if (bugunKayitlar.Count == 0)
        {
            return new EksikFazlaAutoFill(0, 0, 0, 0, 0, 0, false,
                "ℹ️ Bu bölüm Hesap Kontrol modülü çalıştırıldığında kendiliğinden dolacaktır.");
        }

        var aktifKayitlar = bugunKayitlar
            .Where(x => x.HesapTuru != BankaHesapTuru.Stopaj
                     && x.Durum != KayitDurumu.Iptal)
            .ToList();

        decimal ToplamFark(BankaHesapTuru hesap) =>
            aktifKayitlar
                .Where(x => x.HesapTuru == hesap && x.Sinif != FarkSinifi.Beklenen)
                .Sum(x => x.Yon == KayitYonu.Fazla ? x.Tutar : -x.Tutar);

        decimal BeklenenNet(BankaHesapTuru hesap) =>
            aktifKayitlar
                .Where(x => x.HesapTuru == hesap && x.Sinif == FarkSinifi.Beklenen)
                .Sum(x => x.Yon == KayitYonu.Fazla ? x.Tutar : -x.Tutar);

        decimal OlaganDisiNet(BankaHesapTuru hesap) =>
            aktifKayitlar
                .Where(x => x.HesapTuru == hesap && x.Sinif != FarkSinifi.Beklenen)
                .Sum(x => x.Yon == KayitYonu.Fazla ? x.Tutar : -x.Tutar);

        string? BuildBreakdown(BankaHesapTuru hesap)
        {
            var parts = new List<string>();

            var beklenenGruplar = aktifKayitlar
                .Where(x => x.HesapTuru == hesap && x.Sinif == FarkSinifi.Beklenen)
                .GroupBy(x => x.TespitEdilenTip ?? "Diğer")
                .Select(g => new
                {
                    Tip = g.Key switch
                    {
                        "EFT_OTOMATIK_IADE" => "EFT iade",
                        "GELEN_HAVALE" => "Havale",
                        "MEVDUAT_YATIRMA" => "Mevduat",
                        "VIRMAN" => "Virman",
                        "MASRAF" => "Masraf",
                        "HARÇ" => "Harç",
                        "PORTAL" => "Portal",
                        "PARAM EP" => "Param EP",
                        "BAROBİRLİK" => "Barobirlik",
                        _ => g.Key
                    },
                    Tutar = g.Sum(x => x.Yon == KayitYonu.Fazla ? x.Tutar : -x.Tutar)
                })
                .Where(g => Math.Abs(g.Tutar) >= 0.01m)
                .ToList();

            foreach (var g in beklenenGruplar)
                parts.Add($"{g.Tip} {Math.Abs(g.Tutar):N2} ₺");

            var olaganDisi = OlaganDisiNet(hesap);
            if (Math.Abs(olaganDisi) >= 0.01m)
                parts.Add($"Olağan dışı {Math.Abs(olaganDisi):N2} ₺");

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        var oncekiAciklar = await _db.HesapKontrolKayitlari
            .Where(x => x.AnalizTarihi < analizTarihi
                     && x.Durum == KayitDurumu.Acik
                     && x.Yon == KayitYonu.Eksik
                     && x.Sinif != FarkSinifi.Beklenen)
            .ToListAsync(ct);

        var oncekiAcikTahsilat = oncekiAciklar
            .Where(x => x.HesapTuru == BankaHesapTuru.Tahsilat).Sum(x => x.Tutar);
        var oncekiAcikHarc = oncekiAciklar
            .Where(x => x.HesapTuru == BankaHesapTuru.Harc).Sum(x => x.Tutar);

        var bugunCozulenler = await _db.HesapKontrolKayitlari
            .Where(x => x.CozulmeTarihi == analizTarihi
                     && x.Durum == KayitDurumu.Cozuldu
                     && x.AnalizTarihi < analizTarihi
                     && x.Sinif != FarkSinifi.Beklenen)
            .ToListAsync(ct);

        var cozulenTahsilat = bugunCozulenler
            .Where(x => x.HesapTuru == BankaHesapTuru.Tahsilat).Sum(x => x.Tutar);
        var cozulenHarc = bugunCozulenler
            .Where(x => x.HesapTuru == BankaHesapTuru.Harc).Sum(x => x.Tutar);

        var toplamFarkTahsilat = ToplamFark(BankaHesapTuru.Tahsilat);
        var toplamFarkHarc = ToplamFark(BankaHesapTuru.Harc);

        decimal GuneAitNet(BankaHesapTuru hesap) =>
            bugunKayitlar
                .Where(x => x.HesapTuru == hesap
                         && x.Sinif != FarkSinifi.Beklenen
                         && x.Durum == KayitDurumu.Onaylandi)
                .Sum(x => x.Yon == KayitYonu.Fazla ? x.Tutar : -x.Tutar);

        var guneAitTahsilat = GuneAitNet(BankaHesapTuru.Tahsilat);
        var guneAitHarc = GuneAitNet(BankaHesapTuru.Harc);

        var hasData = toplamFarkTahsilat != 0 || toplamFarkHarc != 0
                   || guneAitTahsilat != 0 || guneAitHarc != 0
                   || oncekiAcikTahsilat != 0 || oncekiAcikHarc != 0;

        var bekleyenSayisi = bugunKayitlar.Count(x => x.Sinif != FarkSinifi.Beklenen
                                                   && x.Durum == KayitDurumu.Acik
                                                   && x.HesapTuru != BankaHesapTuru.Stopaj);

        var takipteKayitlar = await _db.HesapKontrolKayitlari
            .Where(x => x.Durum == KayitDurumu.Takipte
                     && x.HesapTuru != BankaHesapTuru.Stopaj)
            .ToListAsync(ct);

        var takipteEksikTahsilat = takipteKayitlar
            .Where(x => x.HesapTuru == BankaHesapTuru.Tahsilat && x.Yon == KayitYonu.Eksik).Sum(x => x.Tutar);
        var takipteEksikHarc = takipteKayitlar
            .Where(x => x.HesapTuru == BankaHesapTuru.Harc && x.Yon == KayitYonu.Eksik).Sum(x => x.Tutar);
        var takipteFazlaTahsilat = takipteKayitlar
            .Where(x => x.HesapTuru == BankaHesapTuru.Tahsilat && x.Yon == KayitYonu.Fazla).Sum(x => x.Tutar);
        var takipteFazlaHarc = takipteKayitlar
            .Where(x => x.HesapTuru == BankaHesapTuru.Harc && x.Yon == KayitYonu.Fazla).Sum(x => x.Tutar);

        string mesaj;
        if (hasData)
            mesaj = "\u2705 Fark kay\u0131tlar\u0131 tespit edildi.";
        else if (bekleyenSayisi > 0)
            mesaj = "\u23f3 " + bekleyenSayisi + " kay\u0131t onay bekliyor \u2014 HesapKontrol sayfas\u0131ndan onaylay\u0131n\u0131z.";
        else
            mesaj = "\u2705 Beklenen d\u0131\u015f\u0131nda fark tespit edilmedi.";

        if (takipteKayitlar.Count > 0)
            mesaj += $" 📌 {takipteKayitlar.Count} kayıt takipte.";

        var beklenenTahsilat = BeklenenNet(BankaHesapTuru.Tahsilat);
        var olaganDisiTahsilat = OlaganDisiNet(BankaHesapTuru.Tahsilat);
        var beklenenHarc = BeklenenNet(BankaHesapTuru.Harc);
        var olaganDisiHarc = OlaganDisiNet(BankaHesapTuru.Harc);

        // ─── Akıllı Takip Korelasyonu ───
        // Bugün takipten çözülen kayıtları getir (CrossDay gelen + el ile çözülen)
        var bugunTakipCozulenler = await _db.HesapKontrolKayitlari
            .Where(x => x.CozulmeTarihi == analizTarihi
                     && x.TakipBaslangicTarihi.HasValue
                     && x.HesapTuru != BankaHesapTuru.Stopaj
                     && (x.Durum == KayitDurumu.Cozuldu || x.Durum == KayitDurumu.Onaylandi))
            .ToListAsync(ct);

        var takipDetaylar = new List<TakipCozumDetay>();
        foreach (var k in bugunTakipCozulenler)
            takipDetaylar.Add(new TakipCozumDetay(k.HesapTuru, k.Tutar, "Geldi", k.AnalizTarihi, k.DosyaNo, k.Aciklama));
        foreach (var k in takipteKayitlar)
            takipDetaylar.Add(new TakipCozumDetay(k.HesapTuru, k.Tutar, "TakipteDevam", k.AnalizTarihi, k.DosyaNo, k.Aciklama));

        string? takipCozumBildirim = null;
        if (bugunTakipCozulenler.Count > 0)
        {
            var toplam = bugunTakipCozulenler.Sum(x => x.Tutar);
            var hesapTipleri = bugunTakipCozulenler
                .GroupBy(x => x.HesapTuru)
                .Select(g => $"{g.Key}: {g.Sum(x => x.Tutar):N2} ₺")
                .ToList();
            takipCozumBildirim = $"✅ Takipten {bugunTakipCozulenler.Count} kayıt geldi ({string.Join(", ", hesapTipleri)}) — toplam {toplam:N2} ₺ çözüldü.";
        }

        return new EksikFazlaAutoFill(
            guneAitTahsilat,
            guneAitHarc,
            oncekiAcikTahsilat,
            oncekiAcikHarc,
            cozulenTahsilat,
            cozulenHarc,
            true,
            mesaj,
            takipteEksikTahsilat,
            takipteEksikHarc,
            takipteFazlaTahsilat,
            takipteFazlaHarc,
            takipteKayitlar.Count,
            BeklenenTahsilat: beklenenTahsilat,
            OlaganDisiTahsilat: olaganDisiTahsilat,
            BeklenenHarc: beklenenHarc,
            OlaganDisiHarc: olaganDisiHarc,
            ToplamFarkTahsilat: toplamFarkTahsilat,
            ToplamFarkHarc: toplamFarkHarc,
            BreakdownMesajTahsilat: BuildBreakdown(BankaHesapTuru.Tahsilat),
            BreakdownMesajHarc: BuildBreakdown(BankaHesapTuru.Harc),
            TakipCozumleri: takipDetaylar.Count > 0 ? takipDetaylar : null,
            TakipCozumBildirim: takipCozumBildirim);
    }
}
