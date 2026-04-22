using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Services.DataFirst;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Calculation.Data;
using KasaManager.Domain.Reports;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

public sealed class FactNormalizationService : IFactNormalizationService
{
    private readonly KasaManagerDbContext _dbContext;
    private readonly ILogger<FactNormalizationService> _logger;

    // Static ConcurrentDictionary — GetOrAdd atomiktir, aynı key için kesinlikle
    // tek SemaphoreSlim garanti eder. Bounded growth: ~10 dosya × 365 gün = ~4000 entry ≈ 100KB.
    // Inline TryRemove race-unsafe olduğu için temizlik yapılmaz; gerekirse ayrı HostedService ile.
    private static readonly ConcurrentDictionary<string, Lazy<SemaphoreSlim>> _locks = new();

    public FactNormalizationService(
        KasaManagerDbContext dbContext,
        ILogger<FactNormalizationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ShadowIngestionResult> NormalizeAndSaveShadowFactsAsync(
        ImportedTable table, DateOnly targetDate, string absoluteFilePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(absoluteFilePath);

        // ── Per-file+date SemaphoreSlim: Aynı dosyanın eşzamanlı işlenmesini önler ──
        var lockKey = $"shadow_lock_{fileName}_{targetDate:yyyyMMdd}";
        var semaphore = _locks.GetOrAdd(lockKey, _ =>
            new Lazy<SemaphoreSlim>(
                () => new SemaphoreSlim(1, 1),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        await semaphore.WaitAsync(ct);
        try
        {
            var fileHash = ComputeFileHash(absoluteFilePath);

            // Idempotency: Bu hash ve tarihe sahip import batch var mı?
            var existingBatch = await _dbContext.ImportBatches
                .FirstOrDefaultAsync(x => x.FileHash == fileHash && x.TargetDate == targetDate, ct);

            if (existingBatch != null)
            {
                _logger.LogInformation("Shadow Ingestion: Aynı dosya (hash) {Date} tarihi için zaten import edilmiş. Atlanıyor.", targetDate);
                return ShadowIngestionResult.Skipped($"Aynı dosya (hash) {targetDate} tarihi için zaten import edilmiş.");
            }

            // --- Fact'lerin üretilmesi (DB dışında, transaction öncesinde hazırlanır) ---
            var batchId = Guid.NewGuid();
            var batch = new ImportBatch
            {
                Id = batchId,
                TargetDate = targetDate,
                SourceFileName = fileName,
                FileHash = fileHash,
                ImportedAt = DateTime.UtcNow,
                ImportProfileVersion = table.Kind.ToString(),
                ImportedBy = "SYSTEM_SHADOW"
            };

            var newFacts = new List<DailyFact>();
            for (int r = 0; r < table.Rows.Count; r++)
            {
                var row = table.Rows[r];
                foreach (var kvp in row)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Value))
                        continue;

                    decimal? numericVal = null;
                    if (decimal.TryParse(kvp.Value, out var parsed))
                        numericVal = parsed;

                    newFacts.Add(new DailyFact
                    {
                        Id = Guid.NewGuid(),
                        ForDate = targetDate,
                        ImportBatchId = batchId,
                        CanonicalKey = $"{table.Kind}_{kvp.Key}".ToLowerInvariant(),
                        RawValue = kvp.Value,
                        TextValue = kvp.Value,
                        NumericValue = numericVal,
                        SourceFileName = fileName,
                        SourceRowNo = r + 1,
                        SourceColumnNo = 0,
                        Confidence = 1.0m
                    });
                }
            }

            // ── Atomik DB operasyonu ──
            // DÜŞÜK: Database.IsRelational() — magic string yerine EF Core API kullanılır.
            if (_dbContext.Database.IsRelational())
            {
                var strategy = _dbContext.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    // KRİTİK 3: ChangeTracker.Clear() — retry sırasında "already tracked"
                    // hatasını önler. Entity'ler her retry'da temiz olarak eklenir.
                    _dbContext.ChangeTracker.Clear();

                    await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);

                    // --- Eski kayıtları temizleme (ExecuteDeleteAsync: belleğe çekmeden atomik silme) ---
                    var previousBatchIds = await _dbContext.ImportBatches
                        .Where(x => x.SourceFileName == fileName && x.TargetDate == targetDate)
                        .Select(x => x.Id)
                        .ToListAsync(ct);

                    if (previousBatchIds.Count > 0)
                    {
                        var deletedFacts = await _dbContext.DailyFacts
                            .Where(x => previousBatchIds.Contains(x.ImportBatchId))
                            .ExecuteDeleteAsync(ct);

                        var deletedBatches = await _dbContext.ImportBatches
                            .Where(x => previousBatchIds.Contains(x.Id))
                            .ExecuteDeleteAsync(ct);

                        _logger.LogInformation(
                            "Shadow Ingestion: {FileName} için {Facts} fact ve {Batches} batch temizlendi.",
                            fileName, deletedFacts, deletedBatches);
                    }

                    // --- Yeni verileri ekle ---
                    _dbContext.ImportBatches.Add(batch);

                    if (newFacts.Count > 0)
                        _dbContext.DailyFacts.AddRange(newFacts);

                    await _dbContext.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                });
            }
            else
            {
                // InMemory fallback (test ortamı)
                var previousBatches = await _dbContext.ImportBatches
                    .Where(x => x.SourceFileName == fileName && x.TargetDate == targetDate)
                    .ToListAsync(ct);

                if (previousBatches.Count > 0)
                {
                    var batchIds = previousBatches.Select(x => x.Id).ToList();
                    var oldFacts = await _dbContext.DailyFacts
                        .Where(x => batchIds.Contains(x.ImportBatchId))
                        .ToListAsync(ct);

                    _dbContext.DailyFacts.RemoveRange(oldFacts);
                    _dbContext.ImportBatches.RemoveRange(previousBatches);
                }

                _dbContext.ImportBatches.Add(batch);

                if (newFacts.Count > 0)
                    _dbContext.DailyFacts.AddRange(newFacts);

                await _dbContext.SaveChangesAsync(ct);
            }

            _logger.LogInformation("Shadow Ingestion: {Count} adet fact eklendi.", newFacts.Count);
            return ShadowIngestionResult.Ok(newFacts.Count);
        }
        catch (Exception ex)
        {
            // YÜKSEK 2: Hata artık caller'a ShadowIngestionResult olarak döndürülüyor.
            // Shadow ingestion hata fırlatmamalı ki eski workflow(Live) bozulmasın.
            _logger.LogError(ex, "Shadow Ingestion başarısız oldu. Dosya: {FileName}", absoluteFilePath);
            return ShadowIngestionResult.Fail(ex.Message);
        }
        finally
        {
            semaphore.Release();
            // Bounded growth kabul edilir — inline TryRemove race-unsafe.
        }
    }

    private string ComputeFileHash(string filePath)
    {
        if (!File.Exists(filePath)) return string.Empty;
        
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = sha256.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
        catch 
        {
            // Hata çıkarsa fallback olarak last write time kullanalım
            return new FileInfo(filePath).LastWriteTimeUtc.Ticks.ToString();
        }
    }
}

