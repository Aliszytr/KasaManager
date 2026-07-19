#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

    public Task<List<HesapKontrolKaydi>> GetTrackedItemsAsync(
        BankaHesapTuru? hesapTuru = null,
        CancellationToken ct = default)
        => GetTrackedItemsCoreAsync(hesapTuru, analizTarihi: null, ct);

    public Task<List<HesapKontrolKaydi>> GetTrackedItemsAsync(
        BankaHesapTuru? hesapTuru,
        DateOnly analizTarihi,
        CancellationToken ct = default)
        => GetTrackedItemsCoreAsync(hesapTuru, analizTarihi, ct);

    private async Task<List<HesapKontrolKaydi>> GetTrackedItemsCoreAsync(
        BankaHesapTuru? hesapTuru,
        DateOnly? analizTarihi,
        CancellationToken ct)
    {
        var query = _db.HesapKontrolKayitlari
            .Where(x => x.Durum == KayitDurumu.Takipte);

        if (hesapTuru.HasValue)
            query = query.Where(x => x.HesapTuru == hesapTuru.Value);
        // Tarihsel status-history yoktur: kayit evreni sinirlanir, mevcut entity durumu kullanilir.
        if (analizTarihi.HasValue)
            query = query.Where(x => x.AnalizTarihi <= analizTarihi.Value);

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
        else
            query = query.Where(x => x.Durum != KayitDurumu.Iptal);

        return await query
            .OrderByDescending(x => x.TakipBaslangicTarihi)
            .ThenBy(x => x.HesapTuru)
            .ThenBy(x => x.Yon)
            .ToListAsync(ct);
    }

    public Task<TakipOzeti> GetTrackingSummaryAsync(CancellationToken ct = default)
        => GetTrackingSummaryCoreAsync(DateOnly.FromDateTime(DateTime.Now), limitByAnalysisDate: false, ct);

    public Task<TakipOzeti> GetTrackingSummaryAsync(DateOnly analizTarihi, CancellationToken ct = default)
        => GetTrackingSummaryCoreAsync(analizTarihi, limitByAnalysisDate: true, ct);

    private async Task<TakipOzeti> GetTrackingSummaryCoreAsync(
        DateOnly contextDate,
        bool limitByAnalysisDate,
        CancellationToken ct)
    {
        var aktifTakipQuery = _db.HesapKontrolKayitlari
            .Where(x => x.Durum == KayitDurumu.Takipte && x.TakipBaslangicTarihi.HasValue);
        var cozulmeQuery = _db.HesapKontrolKayitlari
            .Where(x => x.CozulmeTarihi == contextDate
                     && x.TakipBaslangicTarihi.HasValue
                     && (x.Durum == KayitDurumu.Cozuldu || x.Durum == KayitDurumu.Onaylandi));

        // Tarihsel status-history yoktur: kayit evreni sinirlanir, mevcut entity durumu kullanilir.
        if (limitByAnalysisDate)
        {
            aktifTakipQuery = aktifTakipQuery.Where(x => x.AnalizTarihi <= contextDate);
            cozulmeQuery = cozulmeQuery.Where(x => x.AnalizTarihi <= contextDate);
        }

        var aktifTakip = await aktifTakipQuery.ToListAsync(ct);
        var bugunCozulenler = await cozulmeQuery.ToListAsync(ct);

        var toplamEksik = aktifTakip.Where(x => x.Yon == KayitYonu.Eksik).Sum(x => x.Tutar);
        var toplamFazla = aktifTakip.Where(x => x.Yon == KayitYonu.Fazla).Sum(x => x.Tutar);

        var gunler = aktifTakip
            .Select(x => contextDate.DayNumber - x.TakipBaslangicTarihi!.Value.DayNumber)
            .ToList();
        var ortalamaGun = gunler.Count > 0 ? gunler.Average() : 0;
        var enEskiGun = gunler.Count > 0 ? gunler.Max() : 0;

        var gunBazli = aktifTakip
            .GroupBy(x => contextDate.DayNumber - x.TakipBaslangicTarihi!.Value.DayNumber)
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

    // BUG-SYNC-1 FIX: hesapTuru parametresi eklendi — OpenItems ile aynı filtreleme semantiği.
    public async Task<HesapKontrolDashboard> GetDashboardAsync(DateOnly? analizTarihi = null, BankaHesapTuru? hesapTuru = null, CancellationToken ct = default)
    {
        var acikQuery = _db.HesapKontrolKayitlari
            .Where(x => x.Durum == KayitDurumu.Acik);
        // FIX: Açık kayıtlar aktif iş yüküdür — geçmiş günlerin çözülmemiş kayıtları da dahil.
        // Eski: x.AnalizTarihi == analizTarihi (sadece o günü gösteriyordu, geçmiş açıklar kayboluyordu)
        // Yeni: x.AnalizTarihi <= analizTarihi (kümülatif aktif iş yükü)
        if (analizTarihi.HasValue)
            acikQuery = acikQuery.Where(x => x.AnalizTarihi <= analizTarihi.Value);
        if (hesapTuru.HasValue)
            acikQuery = acikQuery.Where(x => x.HesapTuru == hesapTuru.Value);
        var acikKayitlar = await acikQuery.ToListAsync(ct);

        var takipteQuery = _db.HesapKontrolKayitlari
            .Where(x => x.Durum == KayitDurumu.Takipte);
        // Tarihsel status-history yoktur: kayit evreni sinirlanir, mevcut entity durumu kullanilir.
        if (analizTarihi.HasValue)
            takipteQuery = takipteQuery.Where(x => x.AnalizTarihi <= analizTarihi.Value);
        if (hesapTuru.HasValue)
            takipteQuery = takipteQuery.Where(x => x.HesapTuru == hesapTuru.Value);
        var takipteKayitlar = await takipteQuery.ToListAsync(ct);

        // BUG-3 FIX: Local time kullan (TR UTC+3, gece 00-03 arası yanlış sonuç veriyordu)
        var cozumTarihi = analizTarihi ?? DateOnly.FromDateTime(DateTime.Now);
        var cozulmeCountQuery = _db.HesapKontrolKayitlari
            .Where(x => x.CozulmeTarihi == cozumTarihi
                     && (x.Durum == KayitDurumu.Cozuldu || x.Durum == KayitDurumu.Onaylandi));
        if (analizTarihi.HasValue)
            cozulmeCountQuery = cozulmeCountQuery.Where(x => x.AnalizTarihi <= analizTarihi.Value);
        var bugunCozulen = await cozulmeCountQuery.CountAsync(ct);

        var stopajQuery = _db.HesapKontrolKayitlari
            .Where(x => x.HesapTuru == BankaHesapTuru.Stopaj);
        if (analizTarihi.HasValue)
            stopajQuery = stopajQuery.Where(x => x.AnalizTarihi <= analizTarihi.Value);

        var sonStopaj = await stopajQuery
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
        var summary = new HesapKontrolSnapshotSummary(
            TotalCount: tumKayitlar.Count,
            AcikCount: acik.Count,
            TakipteCount: takipte.Count,
            IptalCount: iptal.Count,
            CozulduCount: cozulen.Count,
            OnaylandiCount: onaylanan.Count,
            ProcessedCount: iptal.Count + cozulen.Count + onaylanan.Count,
            BeklenenCount: tumKayitlar.Count(x => x.Sinif == FarkSinifi.Beklenen),
            BilinmeyenCount: tumKayitlar.Count(x => x.Sinif == FarkSinifi.Bilinmeyen));

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
            summary,
            autoFill,
            mesaj);
    }

    public async Task EnrichComparisonDecisionMemoryAsync(
        ComparisonReport report,
        BankaHesapTuru hesapTuru,
        DateOnly asOfDate,
        CancellationToken ct = default)
    {
        var windowStart = asOfDate.AddDays(-90);
        var memory = await _db.HesapKontrolKayitlari
            .AsNoTracking()
            .Where(x => x.HesapTuru == hesapTuru
                     && x.AnalizTarihi >= windowStart
                     && x.AnalizTarihi <= asOfDate)
            .OrderByDescending(x => x.AnalizTarihi)
            .ThenByDescending(x => x.OlusturmaTarihi)
            .ToListAsync(ct);

        HesapKontrolKaydi? FindLatest(HesapKontrolKaydi probe)
        {
            var fingerprint = GetFollowIdentityFingerprint(probe);
            return memory.FirstOrDefault(x => GetFollowIdentityFingerprint(x) == fingerprint);
        }

        foreach (var surplus in report.SurplusBankaRecords)
        {
            var match = FindLatest(new HesapKontrolKaydi
            {
                HesapTuru = hesapTuru,
                Yon = KayitYonu.Fazla,
                Tutar = Math.Abs(surplus.Tutar),
                Aciklama = surplus.Aciklama
            });

            ApplyDecisionMemory(surplus, match);
        }

        foreach (var missing in report.MissingBankaRecords)
        {
            var match = FindLatest(new HesapKontrolKaydi
            {
                HesapTuru = hesapTuru,
                Yon = KayitYonu.Eksik,
                Tutar = Math.Abs(missing.Miktar),
                DosyaNo = missing.DosyaNo,
                BirimAdi = missing.BirimAdi
            });

            ApplyDecisionMemory(missing, match);
        }
    }

    private static void ApplyDecisionMemory(UnmatchedBankaRecord record, HesapKontrolKaydi? match)
    {
        record.HesapKontrolDurumu = match?.Durum;
        record.HesapKontrolAnalizTarihi = match?.AnalizTarihi;
        record.HesapKontrolNotu = match?.Notlar;
    }

    private static void ApplyDecisionMemory(MissingBankaRecord record, HesapKontrolKaydi? match)
    {
        record.HesapKontrolDurumu = match?.Durum;
        record.HesapKontrolAnalizTarihi = match?.AnalizTarihi;
        record.HesapKontrolNotu = match?.Notlar;
    }

    // ═════════════════════════════════════════════════════════════
    // B6: Auto-Fill (Sabah Kasa Textbox Doldurma)
    // ═════════════════════════════════════════════════════════════

    public async Task<ActiveFollowTotals> GetActiveFollowTotalsAsync(
        DateOnly analizTarihi,
        CancellationToken ct = default)
    {
        var aktifKayitlar = await LoadActiveFollowRecordsAsync(analizTarihi, ct);

        var totals = BuildActiveFollowTotals(analizTarihi, aktifKayitlar);
        LogActiveFollowTotals(totals);
        return totals;
    }

    private Task<List<HesapKontrolKaydi>> LoadActiveFollowRecordsAsync(
        DateOnly analizTarihi,
        CancellationToken ct)
    {
        return _db.HesapKontrolKayitlari
            .Where(x => x.AnalizTarihi <= analizTarihi
                     && x.HesapTuru != BankaHesapTuru.Stopaj
                     && x.Durum == KayitDurumu.Takipte)
            .ToListAsync(ct);
    }

    private static ActiveFollowTotals BuildActiveFollowTotals(
        DateOnly analizTarihi,
        IEnumerable<HesapKontrolKaydi> source)
    {
        var aktifKayitlar = source.ToList();

        decimal Net(BankaHesapTuru hesap) => aktifKayitlar
            .Where(x => x.HesapTuru == hesap && x.Sinif != FarkSinifi.Beklenen)
            .Sum(x => x.Yon == KayitYonu.Fazla ? x.Tutar : -x.Tutar);

        decimal Eksik(BankaHesapTuru hesap) => aktifKayitlar
            .Where(x => x.HesapTuru == hesap && x.Yon == KayitYonu.Eksik && x.Sinif != FarkSinifi.Beklenen)
            .Sum(x => x.Tutar);

        decimal Fazla(BankaHesapTuru hesap) => aktifKayitlar
            .Where(x => x.HesapTuru == hesap && x.Yon == KayitYonu.Fazla && x.Sinif != FarkSinifi.Beklenen)
            .Sum(x => x.Tutar);

        return new ActiveFollowTotals(
            analizTarihi,
            TahsilatNet: Net(BankaHesapTuru.Tahsilat),
            HarcNet: Net(BankaHesapTuru.Harc),
            TahsilatEksik: Eksik(BankaHesapTuru.Tahsilat),
            HarcEksik: Eksik(BankaHesapTuru.Harc),
            TahsilatFazla: Fazla(BankaHesapTuru.Tahsilat),
            HarcFazla: Fazla(BankaHesapTuru.Harc),
            KayitSayisi: aktifKayitlar.Count);
    }

    private void LogActiveFollowTotals(ActiveFollowTotals totals)
    {
        _logger.LogInformation(
            "[HK-ACTIVE-TOTAL-SSOT] Date={Date} IncludedStatuses=Takipte Count={Count} TahsilatNet={TahsilatNet} HarcNet={HarcNet} TahsilatEksik={TahsilatEksik} HarcEksik={HarcEksik} TahsilatFazla={TahsilatFazla} HarcFazla={HarcFazla}",
            totals.AnalizTarihi,
            totals.KayitSayisi,
            totals.TahsilatNet,
            totals.HarcNet,
            totals.TahsilatEksik,
            totals.HarcEksik,
            totals.TahsilatFazla,
            totals.HarcFazla);
    }
    public async Task<EksikFazlaAutoFill> GetAutoFillDataAsync(
        DateOnly analizTarihi,
        CancellationToken ct = default)
    {
        var sources = await LoadAutoFillSourceSetsAsync(analizTarihi, ct);
        return BuildAutoFillSummary(analizTarihi, sources);
    }

    public async Task<HesapKontrolImmutableAuditSnapshot> GetImmutableAuditSnapshotAsync(
        DateOnly analizTarihi,
        CancellationToken ct = default)
    {
        var sources = await LoadAutoFillSourceSetsAsync(analizTarihi, ct);
        var summary = BuildAutoFillSummary(analizTarihi, sources);
        var details = BuildImmutableAuditDetails(sources);
        if (!HesapKontrolImmutableAuditDetailsValidator.TryValidate(details, out var validationError))
            throw new InvalidOperationException(
                $"Immutable Hesap Kontrol audit details validation failed: {validationError}");

        return new HesapKontrolImmutableAuditSnapshot(summary, details);
    }

    private EksikFazlaAutoFill BuildAutoFillSummary(
        DateOnly analizTarihi,
        AutoFillSourceSets sources)
    {
        var bugunKayitlar = sources.BugunKayitlar;

        if (bugunKayitlar.Count == 0
            && sources.OncekiAciklar.Count == 0
            && sources.BugunCozulenler.Count == 0
            && sources.TakipteKayitlar.Count == 0
            && sources.BugunTakipCozulenler.Count == 0)
        {
            return new EksikFazlaAutoFill(0, 0, 0, 0, 0, 0, false,
                "ℹ️ Bu bölüm Hesap Kontrol modülü çalıştırıldığında kendiliğinden dolacaktır.");
        }

        var aktifKayitlar = sources.AktifKayitlar;
        var takipteKayitlar = sources.TakipteKayitlar;


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

        // FIX: Geçmiş günlerden kalan aktif kayıtlar — HEM Eksik HEM Fazla dahil.
        // Eski: x.Yon == KayitYonu.Eksik filtresi Fazla kayıtları (örn. 2.250 TL BİLİNMEYEN) dışarıda bırakıyordu.
        // Yeni: Yon filtresi kaldırıldı. Acik + Takipte durumlar dahil.
        // FarkSinifi.Beklenen (MASRAF, EFT İade vb.) hariç tutulmaya devam ediyor.
        var oncekiAciklar = sources.OncekiAciklar;

        // Net fark hesabı: Fazla = +Tutar, Eksik = -Tutar
        var oncekiAcikTahsilat = oncekiAciklar
            .Where(x => x.HesapTuru == BankaHesapTuru.Tahsilat)
            .Sum(x => x.Yon == KayitYonu.Fazla ? x.Tutar : -x.Tutar);
        var oncekiAcikHarc = oncekiAciklar
            .Where(x => x.HesapTuru == BankaHesapTuru.Harc)
            .Sum(x => x.Yon == KayitYonu.Fazla ? x.Tutar : -x.Tutar);

        _logger.LogInformation(
            "[HK-AUTOFILL-ACTIVE-CARRYOVER] Date={Date} PreviousActiveCount={PrevCount} "
            + "PreviousFazlaCount={PrevFazla} PreviousEksikCount={PrevEksik} "
            + "ExcludedBeklenenFilter=true TahsilatNet={TahsilatNet} HarcNet={HarcNet}",
            analizTarihi,
            oncekiAciklar.Count,
            oncekiAciklar.Count(x => x.Yon == KayitYonu.Fazla),
            oncekiAciklar.Count(x => x.Yon == KayitYonu.Eksik),
            oncekiAcikTahsilat,
            oncekiAcikHarc);

        var bugunCozulenler = sources.BugunCozulenler;

        var cozulenTahsilat = bugunCozulenler
            .Where(x => x.HesapTuru == BankaHesapTuru.Tahsilat).Sum(x => x.Tutar);
        var cozulenHarc = bugunCozulenler
            .Where(x => x.HesapTuru == BankaHesapTuru.Harc).Sum(x => x.Tutar);

        var activeTotals = BuildActiveFollowTotals(analizTarihi, takipteKayitlar);
        LogActiveFollowTotals(activeTotals);
        var toplamFarkTahsilat = activeTotals.TahsilatNet;
        var toplamFarkHarc = activeTotals.HarcNet;
        var resolvedExcluded = bugunKayitlar.Count(x => x.HesapTuru != BankaHesapTuru.Stopaj
                                                     && (x.Durum == KayitDurumu.Cozuldu || x.Durum == KayitDurumu.Onaylandi));
        var cancelledExcluded = bugunKayitlar.Count(x => x.HesapTuru != BankaHesapTuru.Stopaj
                                                      && x.Durum == KayitDurumu.Iptal);

        _logger.LogInformation(
            "[HK-AUTOFILL-SOURCE] TotalRecords={TotalRecords} ActiveIncluded={ActiveIncluded} ResolvedExcluded={ResolvedExcluded} CancelledExcluded={CancelledExcluded} TotalFarkTahsilat={TotalFarkTahsilat} TotalFarkHarc={TotalFarkHarc}",
            bugunKayitlar.Count,
            aktifKayitlar.Count,
            resolvedExcluded,
            cancelledExcluded,
            toplamFarkTahsilat,
            toplamFarkHarc);

        var guneAitTahsilat = activeTotals.TahsilatNet;
        var guneAitHarc = activeTotals.HarcNet;

        _logger.LogInformation(
            "[HK-ACTIVE-TOTAL-APPLIED] Date={Date} Target=AutoFill GuneAitTahsilat={GuneAitTahsilat} GuneAitHarc={GuneAitHarc}",
            analizTarihi,
            guneAitTahsilat,
            guneAitHarc);

        var reconciliationKayitlar = sources.ReconciliationKayitlar;

        var takipKasaEtkisiTahsilat = reconciliationKayitlar
            .Where(x => x.HesapTuru == BankaHesapTuru.Tahsilat && x.Yon == KayitYonu.Eksik)
            .Sum(x => x.Tutar);
        var takipKasaEtkisiHarc = reconciliationKayitlar
            .Where(x => x.HesapTuru == BankaHesapTuru.Harc && x.Yon == KayitYonu.Fazla)
            .Sum(x => x.Tutar);
        var takipKasaEtkisiNet = takipKasaEtkisiTahsilat - takipKasaEtkisiHarc;

        _logger.LogInformation(
            "[RECON-GATEWAY] Date={Date} Scope=Sabah takip_kasa_etkisi_tahsilat={TakipTahsilat} takip_kasa_etkisi_harc={TakipHarc} takip_kasa_etkisi_net={TakipNet}",
            analizTarihi,
            takipKasaEtkisiTahsilat,
            takipKasaEtkisiHarc,
            takipKasaEtkisiNet);

        var hasData = toplamFarkTahsilat != 0 || toplamFarkHarc != 0
                   || guneAitTahsilat != 0 || guneAitHarc != 0
                   || oncekiAcikTahsilat != 0 || oncekiAcikHarc != 0
                   || takipKasaEtkisiNet != 0;

        var bekleyenSayisi = bugunKayitlar.Count(x => x.Sinif != FarkSinifi.Beklenen
                                                   && x.Durum == KayitDurumu.Acik
                                                   && x.HesapTuru != BankaHesapTuru.Stopaj);

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
        var bugunTakipCozulenler = sources.BugunTakipCozulenler;

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
            TakipKasaEtkisiTahsilat: takipKasaEtkisiTahsilat,
            TakipKasaEtkisiHarc: takipKasaEtkisiHarc,
            TakipKasaEtkisiNet: takipKasaEtkisiNet,
            BreakdownMesajTahsilat: BuildBreakdown(BankaHesapTuru.Tahsilat),
            BreakdownMesajHarc: BuildBreakdown(BankaHesapTuru.Harc),
            TakipCozumleri: takipDetaylar.Count > 0 ? takipDetaylar : null,
            TakipCozumBildirim: takipCozumBildirim);
    }

    private async Task<AutoFillSourceSets> LoadAutoFillSourceSetsAsync(
        DateOnly analizTarihi,
        CancellationToken ct)
    {
        var bugunKayitlar = await _db.HesapKontrolKayitlari
            .Where(x => x.AnalizTarihi == analizTarihi)
            .ToListAsync(ct);

        var aktifKayitlar = bugunKayitlar
            .Where(x => x.HesapTuru != BankaHesapTuru.Stopaj
                     && (x.Durum == KayitDurumu.Acik || x.Durum == KayitDurumu.Takipte))
            .ToList();

        var oncekiAciklar = await _db.HesapKontrolKayitlari
            .Where(x => x.AnalizTarihi < analizTarihi
                     && (x.Durum == KayitDurumu.Acik || x.Durum == KayitDurumu.Takipte)
                     && x.HesapTuru != BankaHesapTuru.Stopaj
                     && x.Sinif != FarkSinifi.Beklenen)
            .ToListAsync(ct);

        var bugunCozulenler = await _db.HesapKontrolKayitlari
            .Where(x => x.CozulmeTarihi == analizTarihi
                     && x.Durum == KayitDurumu.Cozuldu
                     && x.AnalizTarihi < analizTarihi
                     && x.Sinif != FarkSinifi.Beklenen)
            .ToListAsync(ct);

        var reconciliationKayitlar = bugunKayitlar
            .Where(x => (x.Durum == KayitDurumu.Cozuldu || x.Durum == KayitDurumu.Onaylandi)
                     && x.Sinif == FarkSinifi.Askida)
            .ToList();

        var takipteKayitlar = await LoadActiveFollowRecordsAsync(analizTarihi, ct);

        var bugunTakipCozulenler = await _db.HesapKontrolKayitlari
            .Where(x => x.CozulmeTarihi == analizTarihi
                     && x.TakipBaslangicTarihi.HasValue
                     && x.HesapTuru != BankaHesapTuru.Stopaj
                     && (x.Durum == KayitDurumu.Cozuldu || x.Durum == KayitDurumu.Onaylandi))
            .ToListAsync(ct);

        return new AutoFillSourceSets(
            bugunKayitlar,
            aktifKayitlar,
            oncekiAciklar,
            bugunCozulenler,
            reconciliationKayitlar,
            takipteKayitlar,
            bugunTakipCozulenler);
    }

    private static HesapKontrolImmutableAuditDetails BuildImmutableAuditDetails(
        AutoFillSourceSets sources)
    {
        var records = new Dictionary<Guid, HesapKontrolImmutableAuditRecord>();

        IReadOnlyList<Guid> Capture(IEnumerable<HesapKontrolKaydi> source)
        {
            var materialized = source.ToList();
            foreach (var entity in materialized)
            {
                var projected = ToImmutableAuditRecord(entity);
                if (records.TryGetValue(projected.KayitId, out var existing)
                    && existing != projected)
                {
                    throw new InvalidOperationException(
                        $"Conflicting immutable audit record view: {projected.KayitId}");
                }

                records[projected.KayitId] = projected;
            }

            return materialized
                .Select(entity => entity.Id)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();
        }

        var groups = new HesapKontrolImmutableAuditGroups(
            Capture(sources.AktifKayitlar),
            Capture(sources.OncekiAciklar),
            Capture(sources.BugunCozulenler),
            Capture(sources.ReconciliationKayitlar),
            Capture(sources.TakipteKayitlar),
            Capture(sources.BugunTakipCozulenler));

        return new HesapKontrolImmutableAuditDetails(
            HesapKontrolImmutableAuditDetailsValidator.OrderRecords(records.Values),
            groups);
    }

    private static HesapKontrolImmutableAuditRecord ToImmutableAuditRecord(
        HesapKontrolKaydi entity) => new(
            entity.Id,
            entity.AnalizTarihi,
            entity.HesapTuru,
            entity.Yon,
            entity.Tutar,
            entity.Durum,
            entity.Sinif,
            entity.DosyaNo,
            entity.BirimAdi,
            entity.TespitEdilenTip,
            entity.TakipBaslangicTarihi,
            entity.CozulmeTarihi,
            entity.OnayTarihi);

    private sealed record AutoFillSourceSets(
        IReadOnlyList<HesapKontrolKaydi> BugunKayitlar,
        IReadOnlyList<HesapKontrolKaydi> AktifKayitlar,
        IReadOnlyList<HesapKontrolKaydi> OncekiAciklar,
        IReadOnlyList<HesapKontrolKaydi> BugunCozulenler,
        IReadOnlyList<HesapKontrolKaydi> ReconciliationKayitlar,
        IReadOnlyList<HesapKontrolKaydi> TakipteKayitlar,
        IReadOnlyList<HesapKontrolKaydi> BugunTakipCozulenler)
    {
        public static AutoFillSourceSets Empty { get; } = new(
            Array.Empty<HesapKontrolKaydi>(),
            Array.Empty<HesapKontrolKaydi>(),
            Array.Empty<HesapKontrolKaydi>(),
            Array.Empty<HesapKontrolKaydi>(),
            Array.Empty<HesapKontrolKaydi>(),
            Array.Empty<HesapKontrolKaydi>(),
            Array.Empty<HesapKontrolKaydi>());
    }
}
