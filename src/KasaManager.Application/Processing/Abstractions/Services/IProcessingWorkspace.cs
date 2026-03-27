using KasaManager.Application.Processing.Abstractions.Repositories;

namespace KasaManager.Application.Processing.Abstractions.Services;

/// <summary>
/// Matching öncesi "çalışma alanı": import/preview sonrası oluşan dataset'leri tutar.
/// DB yokken InMemory implementasyon kullanılır; ileride DB'li implementasyonla değiştirilebilir.
/// 
/// REFACTOR R1: Unified repository eklendi. Legacy repository'ler geçiş süresince korunuyor.
/// </summary>
public interface IProcessingWorkspace
{
    // ===== UNIFIED (Yeni yapı) =====
    /// <summary>
    /// Tüm kasa tiplerini (Sabah/Aksam/Genel) tek repository'de yönetir.
    /// </summary>
    IUnifiedKasaRepository Unified { get; }

    // ===== LEGACY (Geçiş süresince korunuyor) =====
    [Obsolete("Use Unified repository instead")]
    IAksamKasaNesnesiRepository AksamKasa { get; }
    
    [Obsolete("Use Unified repository instead")]
    ISabahKasaNesnesiRepository SabahKasa { get; }
    
    [Obsolete("Use Unified repository instead")]
    IGenelKasaRaporNesnesiRepository GenelKasa { get; }

    IBankaVerilerNesnesiRepository Banka { get; }
    IUYAPVerilerNesnesiRepository Uyap { get; }

    /// <summary>Workspace içindeki tüm datasetleri temizler.</summary>
    void ClearAll();
}

