using System;
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
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

public sealed class FactNormalizationService : IFactNormalizationService
{
    private readonly KasaManagerDbContext _dbContext;
    private readonly ILogger<FactNormalizationService> _logger;

    public FactNormalizationService(
        KasaManagerDbContext dbContext,
        ILogger<FactNormalizationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task NormalizeAndSaveShadowFactsAsync(ImportedTable table, DateOnly targetDate, string absoluteFilePath, CancellationToken ct = default)
    {
        try
        {
            var fileName = Path.GetFileName(absoluteFilePath);
            var fileHash = ComputeFileHash(absoluteFilePath);

            // Idempotency: Bu hash ve tarihe sahip import batch var mı?
            var existingBatch = await _dbContext.ImportBatches
                .FirstOrDefaultAsync(x => x.FileHash == fileHash && x.TargetDate == targetDate, ct);

            if (existingBatch != null)
            {
                _logger.LogInformation("Shadow Ingestion: Aynı dosya (hash) {Date} tarihi için zaten import edilmiş. Atlanıyor.", targetDate);
                return; // Idempotent: Aynı dosyayı tekrar okuma
            }

            // --- Eski kayıtları temizleme (Eğer aynı dosyadan yeni veri geldiyse, eskisini eziyoruz) ---
            var previousBatchesForSameFile = await _dbContext.ImportBatches
                .Where(x => x.SourceFileName == fileName && x.TargetDate == targetDate)
                .ToListAsync(ct);

            if (previousBatchesForSameFile.Any())
            {
                var batchIds = previousBatchesForSameFile.Select(x => x.Id).ToList();
                var oldFacts = await _dbContext.DailyFacts
                    .Where(x => batchIds.Contains(x.ImportBatchId))
                    .ToListAsync(ct);
                
                _dbContext.DailyFacts.RemoveRange(oldFacts);
                _dbContext.ImportBatches.RemoveRange(previousBatchesForSameFile);
                
                _logger.LogInformation("Shadow Ingestion: {FileName} için eski batchler ve fact'ler temizlendi.", fileName);
            }

            // --- Yeni Batch oluşturma ---
            var batch = new ImportBatch
            {
                Id = Guid.NewGuid(),
                TargetDate = targetDate,
                SourceFileName = fileName,
                FileHash = fileHash,
                ImportedAt = DateTime.UtcNow,
                ImportProfileVersion = table.Kind.ToString(), // Gelecekte versionlama stratejisine göre geliştirilecek
                ImportedBy = "SYSTEM_SHADOW"
            };

            _dbContext.ImportBatches.Add(batch);

            // --- Fact'lerin üretilmesi ---
            var newFacts = new List<DailyFact>();
            for (int r = 0; r < table.Rows.Count; r++)
            {
                var row = table.Rows[r];
                foreach (var kvp in row)
                {
                    // Şimdilik Ham değerleri ve varsa parselanmış Numeric Value'ları alıyoruz.
                    // string key'ler CanonicalKey olarak düşürülecek. Boş valueları atlayalım.
                    if (string.IsNullOrWhiteSpace(kvp.Value))
                        continue;

                    decimal? numericVal = null;
                    if (decimal.TryParse(kvp.Value, out var parsed))
                        numericVal = parsed;

                    newFacts.Add(new DailyFact
                    {
                        Id = Guid.NewGuid(),
                        ForDate = targetDate,
                        ImportBatchId = batch.Id,
                        CanonicalKey = $"{table.Kind}_{kvp.Key}".ToLowerInvariant(),
                        RawValue = kvp.Value,
                        TextValue = kvp.Value,
                        NumericValue = numericVal,
                        SourceFileName = fileName,
                        SourceRowNo = r + 1,
                        SourceColumnNo = 0, // Ekstra metadataya göre zenginleştirilebilir.
                        Confidence = 1.0m
                    });
                }
            }

            if (newFacts.Any())
                _dbContext.DailyFacts.AddRange(newFacts);

            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("Shadow Ingestion: {Count} adet fact eklendi.", newFacts.Count);
        }
        catch (Exception ex)
        {
            // Shadow ingestion hata fırlatmamalı ki eski workflow(Live) bozulmasın.
            _logger.LogError(ex, "Shadow Ingestion başarısız oldu. Dosya: {FileName}", absoluteFilePath);
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
