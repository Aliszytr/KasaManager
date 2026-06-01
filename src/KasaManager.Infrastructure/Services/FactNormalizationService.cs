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
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

public sealed class FactNormalizationService : IFactNormalizationService
{
    private readonly KasaManagerDbContext _dbContext;
    private readonly ILogger<FactNormalizationService> _logger;

    // Static ConcurrentDictionary: GetOrAdd is atomic and guarantees a single SemaphoreSlim per key.
    // Bounded growth is acceptable for the current import surface.
    // Inline TryRemove is race-unsafe; cleanup can be handled by a separate HostedService if needed.
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

        // Global lock serializes shadow ingestion against the shared DailyFacts hot table.
        const string GlobalShadowLockKey = "shadow_lock_dailyfacts_global";
        var lockKey = GlobalShadowLockKey;
        var semaphore = _locks.GetOrAdd(lockKey, _ =>
            new Lazy<SemaphoreSlim>(
                () => new SemaphoreSlim(1, 1),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        await semaphore.WaitAsync(ct);
        try
        {
            var fileHash = ComputeFileHash(absoluteFilePath);

            // Idempotency: has this hash already been imported for the target date?
            var existingBatch = await _dbContext.ImportBatches
                .FirstOrDefaultAsync(x => x.FileHash == fileHash && x.TargetDate == targetDate, ct);

            if (existingBatch != null)
            {
                _logger.LogInformation("Shadow Ingestion: Aynı dosya (hash) {Date} tarihi için zaten import edilmiş. Atlanıyor.", targetDate);
                return ShadowIngestionResult.Skipped($"Aynı dosya (hash) {targetDate} tarihi için zaten import edilmiş.");
            }

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

            if (_dbContext.Database.IsRelational())
            {
                var strategy = _dbContext.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    _dbContext.ChangeTracker.Clear();

                    await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);

                    var previousBatchIds = await _dbContext.ImportBatches
                        .Where(x => x.SourceFileName == fileName && x.TargetDate == targetDate)
                        .Select(x => x.Id)
                        .ToListAsync(ct);

                    if (previousBatchIds.Count > 0)
                    {
                        var totalDeletedFacts = await _dbContext.DailyFacts
                            .Where(x => previousBatchIds.Contains(x.ImportBatchId))
                            .ExecuteDeleteAsync(ct);

                        var deletedBatches = await _dbContext.ImportBatches
                            .Where(x => previousBatchIds.Contains(x.Id))
                            .ExecuteDeleteAsync(ct);

                        _logger.LogInformation(
                            "Shadow Ingestion: {FileName} için {Facts} fact ve {Batches} batch temizlendi.",
                            fileName, totalDeletedFacts, deletedBatches);
                    }

                    _dbContext.ImportBatches.Add(batch);

                    if (newFacts.Count > 0)
                        _dbContext.DailyFacts.AddRange(newFacts);

                    await _dbContext.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                });
            }
            else
            {
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
        catch (DbUpdateException ex) when (IsImportBatchDuplicateViolation(ex))
        {
            _logger.LogInformation(ex, "Shadow Ingestion: Same file hash for {Date} was blocked by the DB unique guard. Skipping.", targetDate);
            return ShadowIngestionResult.Skipped($"Aynı dosya (hash) {targetDate} tarihi için zaten import edilmiş.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shadow Ingestion başarısız oldu. Dosya: {FileName}", absoluteFilePath);
            return ShadowIngestionResult.Fail(ex.Message);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static bool IsImportBatchDuplicateViolation(DbUpdateException ex)
    {
        return ex.InnerException switch
        {
            SqlException sqlEx when sqlEx.Number is 2601 or 2627 => true,
            SqliteException sqliteEx when sqliteEx.SqliteErrorCode == 19 => true,
            _ => false
        };
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
            return new FileInfo(filePath).LastWriteTimeUtc.Ticks.ToString();
        }
    }
}