namespace KasaManager.Domain.Helpers;

/// <summary>
/// Finansal hesaplamalar için merkezi yuvarlama politikası.
/// Türk muhasebe standardına uygun olarak MidpointRounding.AwayFromZero kullanır.
/// </summary>
public static class FinancialMath
{
    /// <summary>
    /// Parasal tutarı belirtilen ondalık basamak sayısına yuvarlar.
    /// Varsayılan: 2 basamak (kuruş hassasiyeti).
    /// Yuvarlama: AwayFromZero (Banker's rounding değil — 0.5 → 1.0).
    /// </summary>
    public static decimal Round(decimal value, int decimals = 2)
        => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

    /// <summary>
    /// İki parasal tutarın eşit olup olmadığını belirtilen tolerans ile kontrol eder.
    /// Varsayılan tolerans: 0.01₺ (kuruş farkı).
    /// </summary>
    public static bool AreEqual(decimal a, decimal b, decimal tolerance = 0.01m)
        => Math.Abs(a - b) <= tolerance;
}
