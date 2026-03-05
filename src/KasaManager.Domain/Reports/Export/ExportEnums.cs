namespace KasaManager.Domain.Reports.Export;

/// <summary>
/// Çıktı formatı.
/// </summary>
public enum ExportFormat
{
    Pdf_A4_Portrait,
    Pdf_A4_Landscape,
    Pdf_A5,
    Excel_Xlsx,
    Csv
}

/// <summary>
/// Çıktı içerik türü.
/// </summary>
public enum ExportContent
{
    /// <summary>Genel rapor — tüm sonuçlar + KasaÜstRapor</summary>
    GenelRapor,

    /// <summary>Kompakt özet rapor</summary>
    OzetRapor,

    /// <summary>Sadece KasaÜstRapor tablosu</summary>
    KasaUstRapor,

    /// <summary>Banka yatırma fişi</summary>
    BankaFisi,

    /// <summary>Resmi banka yazısı (şablon tabanlı)</summary>
    BankaYazisi
}
