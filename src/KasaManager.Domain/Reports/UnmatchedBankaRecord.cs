#nullable enable
namespace KasaManager.Domain.Reports;

/// <summary>
/// Online dosyada karşılığı olmayan banka kaydı.
/// Banka dosyasında var ama Online dosyada eşi bulunamayan kayıt.
/// Manuel giriş veya hatalı kayıt olarak değerlendirilir.
/// </summary>
public sealed class UnmatchedBankaRecord
{
    /// <summary>Banka Excel'deki satır indeksi (0-based)</summary>
    public int RowIndex { get; init; }
    
    /// <summary>İşlem tutarı</summary>
    public decimal Tutar { get; init; }
    
    /// <summary>İşlem tarihi</summary>
    public DateTime? Tarih { get; init; }
    
    /// <summary>Banka açıklama metni</summary>
    public string? Aciklama { get; init; }
    
    /// <summary>Tespit edilen kayıt türü ("MASRAF", "HARÇ", "BİLİNMİYOR")</summary>
    public string? DetectedType { get; init; }
    
    /// <summary>Olası sebep açıklaması</summary>
    public string? PossibleReason { get; init; }
    
    /// <summary>Parse edilmiş esas no (varsa)</summary>
    public string? ParsedEsasNo { get; init; }
    
    /// <summary>Parse edilmiş mahkeme (varsa)</summary>
    public string? ParsedMahkeme { get; init; }
}
