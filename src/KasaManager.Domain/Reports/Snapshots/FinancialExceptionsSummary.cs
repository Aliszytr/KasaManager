#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using KasaManager.Domain.FinancialExceptions;

namespace KasaManager.Domain.Reports.Snapshots;

/// <summary>
/// Faz 3: Snapshot'a eklenen Financial Exceptions özet verisi.
/// Bilgi amaçlıdır — hesap motorunun yerine geçmez.
/// </summary>
public sealed class FinancialExceptionsSummary
{
    /// <summary>Snapshot anındaki aktif istisna sayısı.</summary>
    public int ActiveCount { get; set; }

    /// <summary>Toplam onaylı kasa etkisi (signed).</summary>
    public decimal TotalEffect { get; set; }

    /// <summary>İnceleme bekleyen istisna sayısı.</summary>
    public int PendingCount { get; set; }

    /// <summary>Tür bazlı bekleyen tutar toplamları.</summary>
    public Dictionary<string, decimal> TypeBreakdown { get; set; } = new();

    /// <summary>Hesap türü bazlı etki (tahsilat, harc, stopaj).</summary>
    public Dictionary<string, decimal> AccountTypeBreakdown { get; set; } = new();

    /// <summary>Detay açıklamalar.</summary>
    public List<string> Details { get; set; } = new();

    /// <summary>Snapshot almak için JSON serialize.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, _opts);

    /// <summary>JSON'dan deserialize.</summary>
    public static FinancialExceptionsSummary? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<FinancialExceptionsSummary>(json, _opts); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Faz 3: Verilen istisna listesinden snapshot özet verisi oluşturur.
    /// </summary>
    public static FinancialExceptionsSummary Build(IEnumerable<FinansalIstisna> istisnalar)
    {
        var list = istisnalar.ToList();
        var aktifler = list.Where(x => x.Durum != IstisnaDurumu.Cozuldu && x.Durum != IstisnaDurumu.Iptal).ToList();
        var effective = aktifler.Where(BekleyenTutarCalculator.IsRuntimeEffective).ToList();
        var pending = aktifler.Where(x => x.KararDurumu == KararDurumu.IncelemeBekliyor).ToList();

        var summary = new FinancialExceptionsSummary
        {
            ActiveCount = aktifler.Count,
            PendingCount = pending.Count,
        };

        // Toplam etki
        foreach (var ex in effective)
        {
            var bekleyen = BekleyenTutarCalculator.Hesapla(ex);
            var signed = ex.EtkiYonu == KasaEtkiYonu.Azaltan ? -bekleyen : bekleyen;
            summary.TotalEffect += signed;
        }

        // Tür bazlı
        foreach (var g in effective.GroupBy(x => x.Tur))
        {
            var total = g.Sum(x =>
            {
                var b = BekleyenTutarCalculator.Hesapla(x);
                return x.EtkiYonu == KasaEtkiYonu.Azaltan ? -b : b;
            });
            summary.TypeBreakdown[g.Key.ToString()] = total;
        }

        // Hesap türü bazlı
        foreach (var g in effective.GroupBy(x => x.HesapTuru))
        {
            var total = g.Sum(x =>
            {
                var b = BekleyenTutarCalculator.Hesapla(x);
                return x.EtkiYonu == KasaEtkiYonu.Azaltan ? -b : b;
            });
            summary.AccountTypeBreakdown[g.Key.ToString()] = total;
        }

        // Detaylar
        foreach (var ex in effective)
        {
            summary.Details.Add(
                $"{ex.Tur} | {ex.HesapTuru} | {BekleyenTutarCalculator.Hesapla(ex):N2} ₺ | {ex.Neden ?? "—"}");
        }

        return summary;
    }

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
