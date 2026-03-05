#nullable enable
using KasaManager.Domain.Abstractions;

namespace KasaManager.Application.Pipeline;

/// <summary>
/// R20 Wave 1: Unified Data Pipeline Interface.
/// Excel benzeri formül motoru için tek veri kaynağı.
/// Tüm data reader'lar bu pipeline üzerinden akar.
/// </summary>
public interface IDataPipeline
{
    /// <summary>
    /// Pipeline'ı çalıştır ve tüm veri kaynaklarını birleştir.
    /// </summary>
    Task<Result<PipelineResult>> ExecuteAsync(PipelineRequest request, CancellationToken ct = default);
}

/// <summary>
/// Pipeline isteği - hangi verilerin yükleneceğini belirler.
/// </summary>
public sealed record PipelineRequest
{
    /// <summary>Rapor tarihi.</summary>
    public required DateOnly RaporTarihi { get; init; }
    
    /// <summary>Excel dosyalarının bulunduğu klasör.</summary>
    public required string UploadFolder { get; init; }
    
    /// <summary>Kasa tipi (Aksam, Sabah, Genel).</summary>
    public required string KasaScope { get; init; }

    /// <summary>
    /// True Source / Designer senaryosu için: bazı reader'ların "tüm Excel toplamı" okumasını ister.
    /// Varsayılan: false (tek gün / sınırlı okuma).
    /// </summary>
    public bool FullExcelTotals { get; init; }
    
    /// <summary>Kullanıcı girişleri (opsiyonel).</summary>
    public UserInputs? UserInputs { get; init; }
    
    /// <summary>Önceki günden taşınan değerler için tarih aralığı.</summary>
    public DateOnly? RangeStart { get; init; }
    public DateOnly? RangeEnd { get; init; }
}

/// <summary>
/// Pipeline sonucu - tüm veri hücreleri ve metadata.
/// </summary>
public sealed class PipelineResult
{
    /// <summary>Tüm yüklenen veri hücreleri.</summary>
    public IReadOnlyDictionary<string, Cell> Cells { get; init; } = new Dictionary<string, Cell>();
    
    /// <summary>Pipeline çalışma süresi (ms).</summary>
    public long ExecutionTimeMs { get; init; }
    
    /// <summary>Yüklenen kaynak sayısı.</summary>
    public int SourceCount { get; init; }
    
    /// <summary>Uyarılar (kritik olmayan sorunlar).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    
    /// <summary>Debug bilgisi.</summary>
    public IReadOnlyList<string> DebugLog { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Kullanıcı girişleri (manuel değerler).
/// </summary>
public sealed record UserInputs
{
    public decimal? BozukPara { get; init; }
    public decimal? NakitPara { get; init; }
    public decimal? VergidenGelen { get; init; }
    public decimal? GelmeyenD { get; init; }
    public decimal? KasadaKalacakHedef { get; init; }
    public decimal? KaydenTahsilat { get; init; }
    public decimal? KaydenHarc { get; init; }
    public decimal? BankadanCekilen { get; init; }
    public decimal? CesitliNedenlerleBankadanCikamayanTahsilat { get; init; }
    public decimal? BankayaGonderilmisDeger { get; init; }
    public decimal? BankayaYatirilacakHarciDegistir { get; init; }
    public decimal? BankayaYatirilacakTahsilatiDegistir { get; init; }
}
