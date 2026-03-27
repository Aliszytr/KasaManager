#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.FinancialExceptions;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// Faz 3: Anomaly Suggestion implementasyonu.
/// HesapKontrol anomali kalıplarını analiz eder ve operatöre öneri sunar.
/// ARCHITECTURE LOCK: Otomatik istisna oluşturmaz.
/// </summary>
public sealed class FinansalIstisnaAnomaliService : IFinansalIstisnaAnomaliService
{
    private readonly KasaManagerDbContext _db;
    private readonly ILogger<FinansalIstisnaAnomaliService> _log;

    public FinansalIstisnaAnomaliService(KasaManagerDbContext db, ILogger<FinansalIstisnaAnomaliService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<IReadOnlyList<AnomaliOnerisi>> AnalyzeAsync(DateOnly tarih, CancellationToken ct = default)
    {
        var oneriler = new List<AnomaliOnerisi>();

        // OB-3 FIX: Her kural bağımsız try/catch — bir kural patlarsa diğerleri çalışır
        // ── Kural 1: Uzun süredir açık HesapKontrol kayıtları ──
        try { await CheckLongOpenRecordsAsync(tarih, oneriler, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "Anomali Kural-1 (uzun süredir açık kayıtlar) başarısız"); }

        // ── Kural 2: Tekrarlayan fark kalıpları ──
        try { await CheckRepeatingPatternsAsync(tarih, oneriler, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "Anomali Kural-2 (tekrarlayan kalıplar) başarısız"); }

        // ── Kural 3: Yüksek tutarlı çözülmemiş farklar ──
        try { await CheckHighValueOpenAsync(tarih, oneriler, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "Anomali Kural-3 (yüksek tutarlı açık farklar) başarısız"); }

        return oneriler;
    }

    /// <summary>3+ gündür açık HesapKontrol farkları → gecikmeli banka hareketi olabilir.</summary>
    private async Task CheckLongOpenRecordsAsync(DateOnly tarih, List<AnomaliOnerisi> oneriler, CancellationToken ct)
    {
        var threshold = tarih.AddDays(-3);
        var longOpen = await _db.HesapKontrolKayitlari
            .Where(k => k.Durum == KayitDurumu.Acik
                        && k.AnalizTarihi <= threshold)
            .OrderBy(k => k.AnalizTarihi)
            .Take(5)
            .ToListAsync(ct);

        foreach (var k in longOpen)
        {
            var gunSayisi = tarih.DayNumber - k.AnalizTarihi.DayNumber;
            oneriler.Add(new AnomaliOnerisi
            {
                Baslik = $"⏳ {gunSayisi} gündür açık fark",
                Aciklama = $"{k.HesapTuru} hesabında {k.Tutar:N2} ₺ tutarında {k.Yon} fark {gunSayisi} gündür açık. " +
                           $"Bu bir GecikmeliBankaHareketi olabilir.",
                OnerilenTur = IstisnaTuru.GecikmeliBankaHareketi,
                OnerilenHesapTuru = k.HesapTuru,
                OnerilenTutar = k.Tutar,
                GuvenSeviyesi = Math.Min(0.5 + (gunSayisi * 0.1), 0.95),
                Kaynak = "HesapKontrol",
                KaynakKayitId = k.Id
            });
        }
    }

    /// <summary>Aynı hesap + tutarda tekrarlayan farklar → sistemik sorun olabilir.</summary>
    private async Task CheckRepeatingPatternsAsync(DateOnly tarih, List<AnomaliOnerisi> oneriler, CancellationToken ct)
    {
        var son30Gun = tarih.AddDays(-30);
        var groups = await _db.HesapKontrolKayitlari
            .Where(k => k.AnalizTarihi >= son30Gun
                     && (k.Durum == KayitDurumu.Acik || k.Durum == KayitDurumu.Takipte))
            .GroupBy(k => new { k.HesapTuru, k.Tutar })
            .Where(g => g.Count() >= 3)
            .Select(g => new
            {
                g.Key.HesapTuru, g.Key.Tutar,
                Tekrar = g.Count(),
                SonTarih = g.Max(x => x.AnalizTarihi)
            })
            .OrderByDescending(g => g.Tekrar)
            .Take(3)
            .ToListAsync(ct);

        foreach (var g in groups)
        {
            oneriler.Add(new AnomaliOnerisi
            {
                Baslik = $"🔄 Tekrarlayan fark: {g.Tutar:N2} ₺",
                Aciklama = $"{g.HesapTuru} hesabında {g.Tutar:N2} ₺ tutarında fark son 30 günde {g.Tekrar} kez tekrarlamış. " +
                           $"Sistemik bir sorun olabilir.",
                OnerilenTur = IstisnaTuru.BasarisizVirman,
                OnerilenHesapTuru = g.HesapTuru,
                OnerilenTutar = g.Tutar,
                GuvenSeviyesi = Math.Min(0.4 + (g.Tekrar * 0.15), 0.9),
                Kaynak = "HesapKontrol Kalıp Analizi"
            });
        }
    }

    /// <summary>Yüksek tutarlı (1000+) çözülmemiş açık kayıtlar.</summary>
    private async Task CheckHighValueOpenAsync(DateOnly tarih, List<AnomaliOnerisi> oneriler, CancellationToken ct)
    {
        var highValue = await _db.HesapKontrolKayitlari
            .Where(k => k.Durum == KayitDurumu.Acik && k.Tutar >= 1000m)
            .OrderByDescending(k => k.Tutar)
            .Take(3)
            .ToListAsync(ct);

        foreach (var k in highValue)
        {
            oneriler.Add(new AnomaliOnerisi
            {
                Baslik = $"⚠️ Yüksek tutarlı açık fark: {k.Tutar:N2} ₺",
                Aciklama = $"{k.HesapTuru} hesabında {k.Tutar:N2} ₺ tutarında {k.Yon} fark operasyonel istisna olarak değerlendirilebilir.",
                OnerilenTur = k.Yon == KayitYonu.Eksik
                    ? IstisnaTuru.BankadanCikamayanTutar
                    : IstisnaTuru.SistemeGirilmeyenEft,
                OnerilenHesapTuru = k.HesapTuru,
                OnerilenTutar = k.Tutar,
                GuvenSeviyesi = 0.6,
                Kaynak = "HesapKontrol",
                KaynakKayitId = k.Id
            });
        }
    }
}
