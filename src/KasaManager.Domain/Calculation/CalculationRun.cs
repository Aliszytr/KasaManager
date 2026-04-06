using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Guards;

namespace KasaManager.Domain.Calculation;

/// <summary>
/// Bir hesaplama çalıştırmasının (Run) izlenebilir kaydı.
/// R16 başlangıcında Preview/Simülasyon için UI'ya döndürülür.
/// </summary>
public sealed class CalculationRun
{
    public Guid RunId { get; init; } = Guid.NewGuid();
    public DateTime RunDateTimeUtc { get; init; } = DateTime.UtcNow;
    public DateOnly ReportDate { get; init; }

    public string FormulaSetId { get; init; } = string.Empty;
    public string FormulaSetVersion { get; init; } = string.Empty;

    /// <summary>
    /// Kullanılan ham girişler (UnifiedPool) + override.
    /// Decimal map: canonical_key -> decimal.
    /// </summary>
    public Dictionary<string, decimal> Inputs { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Çalıştırma sırasında kullanılan override'lar (kullanıcı girişleri).
    /// Hamı değiştirmez; sadece hesap katmanına etki eder.
    /// </summary>
    public Dictionary<string, decimal> Overrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Hesaplanan çıktılar.
    /// </summary>
    public Dictionary<string, decimal> Outputs { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Açıklanabilirlik: her çıktı için hesap adımları.
    /// </summary>
    public List<CalculationExplainItem> Explain { get; init; } = new();

    public List<GuardResult> GuardResults { get; init; } = new();

    /// <summary>
    /// P1(C): Hesaplama sırasında üretilen uyarılar (undefined variable → 0, vb.)
    /// </summary>
    public List<string>? Warnings { get; set; }
}

public sealed class CalculationExplainItem
{
    public string TargetKey { get; init; } = string.Empty;
    public string Expression { get; init; } = string.Empty;
    public Dictionary<string, decimal> ResolvedVariables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public decimal Result { get; init; }
}
