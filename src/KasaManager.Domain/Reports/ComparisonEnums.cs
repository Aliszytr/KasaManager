#nullable enable
namespace KasaManager.Domain.Reports;

/// <summary>
/// Karşılaştırma eşleşme durumu.
/// </summary>
public enum MatchStatus
{
    /// <summary>Tam eşleşme bulundu (Confidence >= 0.8)</summary>
    Matched = 0,
    
    /// <summary>Kısmi eşleşme (Confidence >= 0.5 ve < 0.8)</summary>
    PartialMatch = 1,
    
    /// <summary>Eşleşme bulunamadı</summary>
    NotFound = 2,
    
    /// <summary>Birden fazla olası eşleşme bulundu</summary>
    MultipleMatches = 3
}

/// <summary>
/// Karşılaştırma türü.
/// </summary>
public enum ComparisonType
{
    /// <summary>BankaTahsilat(+) vs onlineMasraf - Giren</summary>
    TahsilatMasraf = 0,
    
    /// <summary>BankaHarc(+) vs onlineHarc - Giren</summary>
    HarcamaHarc = 1,
    
    /// <summary>BankaTahsilat(-) vs onlineReddiyat - Çıkan</summary>
    ReddiyatCikis = 2
}
