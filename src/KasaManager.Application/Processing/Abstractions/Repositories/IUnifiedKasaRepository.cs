#nullable enable
using KasaManager.Domain.Models;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Processing.Abstractions.Repositories;

/// <summary>
/// REFACTOR R1: Unified Kasa repository interface.
/// 
/// Tüm kasa tiplerini (Sabah, Aksam, Genel) tek bir repository'de yönetir.
/// UnifiedKasaRecord dictionary-based model kullanır.
/// 
/// Bu interface, legacy marker interface'lerin (IAksamKasaNesnesiRepository vb.)
/// yerine kullanılabilir.
/// </summary>
public interface IUnifiedKasaRepository
{
    /// <summary>
    /// Belirtilen tarih ve tür için kayıt ekle veya güncelle.
    /// </summary>
    void Upsert(UnifiedKasaRecord record);

    /// <summary>
    /// Belirtilen tarih ve tür için kayıt getir.
    /// </summary>
    UnifiedKasaRecord? Get(DateOnly raporTarihi, KasaRaporTuru turu);

    /// <summary>
    /// Belirtilen tarih aralığındaki kayıtları getir.
    /// </summary>
    IReadOnlyList<UnifiedKasaRecord> GetByDateRange(DateOnly startDate, DateOnly endDate, KasaRaporTuru? turu = null);

    /// <summary>
    /// Belirtilen türdeki tüm kayıtları getir.
    /// </summary>
    IReadOnlyList<UnifiedKasaRecord> GetByType(KasaRaporTuru turu);

    /// <summary>
    /// Tüm kayıtları getir.
    /// </summary>
    IReadOnlyList<UnifiedKasaRecord> GetAll();

    /// <summary>
    /// Belirtilen tarih ve tür için kaydı sil.
    /// </summary>
    bool Remove(DateOnly raporTarihi, KasaRaporTuru turu);

    /// <summary>
    /// Tüm kayıtları temizle.
    /// </summary>
    void Clear();

    /// <summary>
    /// Kayıt sayısını getir.
    /// </summary>
    int Count { get; }
}
