namespace KasaManager.Domain.Reports;

/// <summary>
/// R6 Snapshot rapor türü.
/// R17: Ortak Kasa desteği eklendi.
/// </summary>
public enum KasaRaporTuru
{
    Sabah = 1,
    Aksam = 2,
    Genel = 3,
    Ortak = 4  // R17: Ortak Kasa için
}
