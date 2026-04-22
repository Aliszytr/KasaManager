using System;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services.DataFirst;

/// <summary>
/// Shadow Ingestion sonuç modeli.
/// </summary>
public sealed record ShadowIngestionResult(
    bool Success,
    int RowsInserted,
    string? Error = null)
{
    public static ShadowIngestionResult Ok(int rows) => new(true, rows);
    public static ShadowIngestionResult Skipped(string reason) => new(true, 0, reason);
    public static ShadowIngestionResult Fail(string error) => new(false, 0, error);
}

public interface IFactNormalizationService
{
    /// <summary>
    /// R17 -> Data-First Geçişi: Excelden okunan tabloyu DailyFact kayıtlarına dönüştürerek
    /// DB'ye kaydeder. Mevcut sistemi kırmamak için arka planda "Shadow Mode" olarak çalışır.
    /// Aynı dosya için idempotency (tekrarlanabilirlik) kurallarını uygular.
    /// </summary>
    Task<ShadowIngestionResult> NormalizeAndSaveShadowFactsAsync(ImportedTable table, DateOnly targetDate, string absoluteFilePath, CancellationToken ct = default);
}

