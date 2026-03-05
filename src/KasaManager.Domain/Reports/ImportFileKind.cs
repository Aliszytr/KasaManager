namespace KasaManager.Domain.Reports;

/// <summary>
/// Kurumun günlük yenilenen 6+ sabit rapor ailesi.
/// İsimler değişebilir; orchestrator dosya adına göre tahmin edebilir.
/// </summary>
public enum ImportFileKind
{
    Unknown = 0,
    BankaHarcama = 1,
    BankaTahsilat = 2,
    KasaUstRapor = 3,
    MasrafVeReddiyat = 4,
    OnlineHarcama = 5,
    OnlineMasraf = 6,
    OnlineReddiyat = 7
}
