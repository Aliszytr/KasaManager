#nullable enable
using System.Globalization;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Calculation;
using KasaManager.Domain.Constants;
using KasaManager.Domain.Reports;

namespace KasaManager.Infrastructure.Export;

/// <summary>
/// CalculationRun → KasaRaporData dönüştürücü.
/// 
/// KRİTİK: Request.Form, hidden input, UI verisi KULLANMAZ.
/// Tek kaynak: CalculationRun.Outputs/Inputs + GlobalDefaults (IBAN).
/// </summary>
public sealed class ReportDataBuilder : IReportDataBuilder
{
    private readonly IKasaGlobalDefaultsService _defaults;

    public ReportDataBuilder(IKasaGlobalDefaultsService defaults)
    {
        _defaults = defaults;
    }

    public async Task<KasaRaporData> BuildAsync(
        CalculationRun run,
        string kasaTuru,
        ImportedTable? ustRaporTable,
        CancellationToken ct)
    {
        var defaults = await _defaults.GetAsync(ct);
        var isSabah = kasaTuru.Equals("Sabah", StringComparison.OrdinalIgnoreCase);

        var data = new KasaRaporData
        {
            Tarih = run.ReportDate,
            KasaTuru = kasaTuru,
            KasayiYapan = null, // Caller (Controller) tarafından set edilecek

            // ══ ROW 1: Dünden Devreden + Genel Kasa ══
            DundenDevredenKasa = Get(run, KasaCanonicalKeys.DundenDevredenKasa),
            GenelKasa = Get(run, KasaCanonicalKeys.GenelKasa),

            // ══ ROW 2: Reddiyat + Bankadan Çıkan + Stopaj ══
            OnlineReddiyat = Get(run, KasaCanonicalKeys.ToplamReddiyat),
            BankadanCikan = Get(run, KasaCanonicalKeys.BankaCikanTahsilat),
            ToplamStopaj = Get(run, KasaCanonicalKeys.ToplamStopaj),

            // ══ ROW 3: Bankaya Götürülecek ══
            BankayaStopaj = Get(run, KasaCanonicalKeys.BankayaYatirilacakStopaj),
            BankayaTahsilat = Get(run, KasaCanonicalKeys.BankayaYatirilacakNakit),
            BankayaHarc = Get(run, KasaCanonicalKeys.BankayaYatirilacakHarc),

            // IBAN bilgileri — GlobalDefaults
            HesapAdiStopaj = defaults.HesapAdiStopaj,
            IbanStopaj = defaults.IbanStopaj,
            HesapAdiTahsilat = defaults.HesapAdiMasraf,
            IbanTahsilat = defaults.IbanMasraf,
            HesapAdiHarc = defaults.HesapAdiHarc,
            IbanHarc = defaults.IbanHarc,

            // Devir
            KasadakiNakit = Get(run, KasaCanonicalKeys.KasaNakit),
            DundenDevredenBanka = Get(run, KasaCanonicalKeys.DundenDevredenBanka),
            YarinaDevredecekBanka = Get(run, KasaCanonicalKeys.YarinaDeverecekBanka),

            // Vergi
            VergidenGelen = Get(run, KasaCanonicalKeys.VergiGelenKasa),
            VergiKasa = Get(run, KasaCanonicalKeys.VergiKasa),
            VergideBirikenKasa = 0, // Hesaplanacak aşağıda

            // Beklenen Girişler
            EftOtomatikIade = Get(run, KasaCanonicalKeys.EftOtomatikIade),
            GelenHavale = Get(run, KasaCanonicalKeys.GelenHavale),
            IadeKelimesiGiris = Get(run, KasaCanonicalKeys.IadeKelimesiGiris),

            // Eksik/Fazla
            IsSabahKasa = isSabah,
            GuneAitEksikFazlaTahsilat = Get(run, KasaCanonicalKeys.GuneAitEksikFazlaTahsilat),
            DundenEksikFazlaTahsilat = Get(run, KasaCanonicalKeys.DundenEksikFazlaTahsilat),
            DundenEksikFazlaGelenTahsilat = Get(run, KasaCanonicalKeys.DundenEksikFazlaGelenTahsilat),
            GuneAitEksikFazlaHarc = Get(run, KasaCanonicalKeys.GuneAitEksikFazlaHarc),
            DundenEksikFazlaHarc = Get(run, KasaCanonicalKeys.DundenEksikFazlaHarc),
            DundenEksikFazlaGelenHarc = Get(run, KasaCanonicalKeys.DundenEksikFazlaGelenHarc),
        };

        // Stopaj kontrolü
        var stopajFark = data.OnlineReddiyat - data.BankadanCikan - data.ToplamStopaj;
        data.StopajKontrolOk = Math.Abs(stopajFark) < 0.01m;
        data.StopajKontrolFark = stopajFark;

        // BankayaToplam = Stopaj + Tahsilat + Harç
        data.BankayaToplam = data.BankayaStopaj + data.BankayaTahsilat + data.BankayaHarc;

        // VergideBirikenKasa = Seed + VergiKasa − VergidenGelen
        // Seed: Genel Kasa'dan aktarılır, rapor kaydedildiğinde carry-forward yapılır
        var vergideBirikenSeed = defaults.VergideBirikenSeed ?? 0m;
        data.VergideBirikenKasa = vergideBirikenSeed + data.VergiKasa - data.VergidenGelen;

        // KasaÜstRapor tablosu
        if (ustRaporTable != null)
        {
            HydrateUstRapor(data, ustRaporTable);
        }

        return data;
    }

    public async Task<KasaRaporData> BuildFromSnapshotAsync(Guid snapshotId, CancellationToken ct)
    {
        // Gelecek: CalculatedKasaSnapshot'tan veri çekilecek
        // Şimdilik boş bir KasaRaporData döndür
        await Task.CompletedTask;
        return new KasaRaporData
        {
            Tarih = DateOnly.FromDateTime(DateTime.Today),
            KasaTuru = "Bilinmiyor",
            Aciklama = $"Snapshot {snapshotId} — henüz implement edilmedi"
        };
    }

    // ═════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════

    /// <summary>
    /// CalculationRun'dan bir canonical key değerini okur.
    /// Öncelik: Outputs → Overrides → Inputs → 0
    /// </summary>
    private static decimal Get(CalculationRun run, string key)
    {
        if (run.Outputs.TryGetValue(key, out var o)) return o;
        if (run.Overrides.TryGetValue(key, out var ov)) return ov;
        if (run.Inputs.TryGetValue(key, out var i)) return i;
        return 0m;
    }

    /// <summary>
    /// ImportedTable → UstRaporSatir listesi.
    /// </summary>
    private static void HydrateUstRapor(KasaRaporData data, ImportedTable table)
    {
        // Veznedar kolonu bul
        var vezCol = "VEZNEDAR";
        if (table.ColumnMetas?.Count > 0)
        {
            var candidates = new[] { "veznedar", "vezne", "kasiyer", "personel", "ad" };
            foreach (var c in candidates)
            {
                var hit = table.ColumnMetas.FirstOrDefault(m =>
                    string.Equals(m.CanonicalName, c, StringComparison.OrdinalIgnoreCase));
                if (hit != null) { vezCol = hit.CanonicalName; break; }
            }
        }

        // Veznedar dışı kolonları ekle
        data.UstRaporKolonlar = table.Columns
            .Where(c => !c.Equals(vezCol, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var row in table.Rows)
        {
            row.TryGetValue(vezCol, out var vezAdi);
            data.UstRaporSatirlar.Add(new UstRaporSatir
            {
                VeznedarAdi = vezAdi ?? "",
                Degerler = new Dictionary<string, string?>(row)
            });
        }
    }
}
