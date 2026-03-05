#nullable enable
using KasaManager.Domain.Abstractions;

namespace KasaManager.Application.Pipeline;

/// <summary>
/// R20 Wave 3: Formula Context Factory interface.
/// FormulaContext oluşturma ve yapılandırma sorumluluğu.
/// </summary>
public interface IFormulaContextFactory
{
    /// <summary>
    /// Yeni FormulaContext oluştur (veri pipeline ile doldurulmuş).
    /// </summary>
    Task<Result<FormulaContext>> CreateAsync(FormulaContextRequest request, CancellationToken ct = default);
    
    /// <summary>
    /// Mevcut FormulaSet'ten context oluştur.
    /// </summary>
    Task<Result<FormulaContext>> CreateFromFormulaSetAsync(Guid formulaSetId, DateOnly raporTarihi, string uploadFolder, CancellationToken ct = default);
}

/// <summary>
/// FormulaContext oluşturma isteği.
/// </summary>
public sealed record FormulaContextRequest
{
    /// <summary>Rapor tarihi.</summary>
    public required DateOnly RaporTarihi { get; init; }
    
    /// <summary>Excel dosyalarının bulunduğu klasör.</summary>
    public required string UploadFolder { get; init; }
    
    /// <summary>Kasa tipi (Aksam, Sabah, Genel).</summary>
    public required string KasaScope { get; init; }
    
    /// <summary>Kullanıcı girişleri (opsiyonel).</summary>
    public UserInputs? UserInputs { get; init; }
    
    /// <summary>Tarih aralığı başlangıcı (Genel Kasa için).</summary>
    public DateOnly? RangeStart { get; init; }
    
    /// <summary>Tarih aralığı sonu.</summary>
    public DateOnly? RangeEnd { get; init; }
    
    /// <summary>Mevcut FormulaSet ID (yükleme için).</summary>
    public Guid? FormulaSetId { get; init; }
}
