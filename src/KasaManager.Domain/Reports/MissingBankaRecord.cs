#nullable enable
namespace KasaManager.Domain.Reports;

/// <summary>
/// Banka dosyasında karşılığı olmayan online kayıt.
/// Online dosyada var ama Banka dosyasında eşi bulunamayan kayıt.
/// Bedeli gelmeyen/yatırılmayan işlem olarak değerlendirilir.
/// </summary>
public sealed class MissingBankaRecord
{
    /// <summary>Online Excel'deki satır indeksi (0-based)</summary>
    public int RowIndex { get; init; }
    
    /// <summary>Dosya numarası (örn: "2026/36")</summary>
    public string? DosyaNo { get; init; }
    
    /// <summary>Birim adı (örn: "Ankara 22. İdare Mahkemesi")</summary>
    public string? BirimAdi { get; init; }
    
    /// <summary>Beklenen miktar</summary>
    public decimal Miktar { get; init; }
    
    /// <summary>İşlem tarihi</summary>
    public DateTime? Tarih { get; init; }
    
    /// <summary>Durum açıklaması</summary>
    public string? Reason { get; init; }
}
