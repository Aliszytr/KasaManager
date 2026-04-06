using System;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services.DataFirst;

public interface IFactNormalizationService
{
    /// <summary>
    /// R17 -> Data-First Geçişi: Excelden okunan tabloyu DailyFact kayıtlarına dönüştürerek
    /// DB'ye kaydeder. Mevcut sistemi kırmamak için arka planda "Shadow Mode" olarak çalışır.
    /// Aynı dosya için idempotency (tekrarlanabilirlik) kurallarını uygular.
    /// </summary>
    Task NormalizeAndSaveShadowFactsAsync(ImportedTable table, DateOnly targetDate, string absoluteFilePath, CancellationToken ct = default);
}
