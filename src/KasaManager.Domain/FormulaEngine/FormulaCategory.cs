namespace KasaManager.Domain.FormulaEngine;

public enum FormulaCategory
{
    Unknown = 0,
    Tahsilat = 1,
    Harc = 2,
    Reddiyat = 3,
    Stopaj = 4,
    Banka = 5,
    Kasa = 6,
    /// <summary>
    /// Genel Kasa / sistem geneli icin hesaplar.
    /// (UI'da ayri bucket gostermek icin eklenmistir.)
    /// </summary>
    Genel = 7,
    Meta = 99
}
