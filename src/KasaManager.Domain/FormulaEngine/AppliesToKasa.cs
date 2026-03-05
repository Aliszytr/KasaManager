namespace KasaManager.Domain.FormulaEngine;

/// <summary>
/// Hangi kasa raporu için formül seti uygulanacağını belirtir.
/// Not: R16 başlangıcında Preview/Simülasyon için kullanılır.
/// </summary>
public enum AppliesToKasa
{
    Unknown = 0,
    Genel = 1,
    Sabah = 2,
    Aksam = 3,
    Any = 9
}
