using KasaManager.Application.Abstractions;
using KasaManager.Domain.FormulaEngine.Authoring;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Infrastructure.Persistence;

public sealed class FormulaSetStore : IFormulaSetStore
{
    private readonly KasaManagerDbContext _db;

    public FormulaSetStore(KasaManagerDbContext db)
    {
        _db = db;
    }

    public async Task<List<PersistedFormulaSet>> ListAsync(CancellationToken ct = default)
    {
        // Lines çok olduğundan listeleme sade olsun (Include yok).
        return await _db.FormulaSets
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<PersistedFormulaSet?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.FormulaSets
            .Include(x => x.Lines.OrderBy(l => l.SortOrder))
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<PersistedFormulaSet> CreateAsync(PersistedFormulaSet set, CancellationToken ct = default)
    {
        set.Id = set.Id == Guid.Empty ? Guid.NewGuid() : set.Id;
        set.CreatedAtUtc = set.CreatedAtUtc == default ? DateTime.UtcNow : set.CreatedAtUtc;
        set.UpdatedAtUtc = DateTime.UtcNow;

        // Request lines dedupe (Id bazında + (TargetKey, Mode, SourceKey) bazında)
        var normalizedIncoming = new List<PersistedFormulaLine>();
        var seenIds = new HashSet<Guid>();
        var seenComposite = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in (set.Lines ?? new List<PersistedFormulaLine>()))
        {
            // CREATE her zaman yeni satır Id'leri üretir.
            // UI/Client yanlışlıkla DB'den gelmiş eski Id'leri gönderse bile PK duplicate'a girmesin.
            var id = Guid.NewGuid();
            if (!seenIds.Add(id))
                continue;

            var target = (raw.TargetKey ?? string.Empty).Trim();
            var mode = (raw.Mode ?? "Map").Trim();
            var source = (raw.SourceKey ?? string.Empty).Trim();

            var compositeKey = $"{target}||{mode}||{source}";
            if (!seenComposite.Add(compositeKey))
                continue;

            normalizedIncoming.Add(new PersistedFormulaLine
            {
                Id = id,
                SetId = set.Id,
                TargetKey = target,
                Mode = mode,
                SourceKey = string.IsNullOrWhiteSpace(raw.SourceKey) ? null : raw.SourceKey.Trim(),
                Expression = string.IsNullOrWhiteSpace(raw.Expression) ? null : raw.Expression.Trim(),
                SortOrder = raw.SortOrder,
                IsHidden = raw.IsHidden
            });
        }

        set.Lines = normalizedIncoming
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.TargetKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _db.FormulaSets.Add(set);
        await _db.SaveChangesAsync(ct);
        return set;
    }

    
    public async Task<PersistedFormulaSet> UpdateAsync(PersistedFormulaSet set, CancellationToken ct = default)
    {
        if (set.Id == Guid.Empty)
            throw new ArgumentException("FormulaSet.Id boş olamaz (update).", nameof(set));

        // SQL Server retry strategy (EnableRetryOnFailure) açıksa, user-initiated transaction
        // EF Core'un ExecutionStrategy'si içinde çalıştırılmalıdır.
        // Aksi halde: SqlServerRetryingExecutionStrategy does not support user-initiated transactions.
        var strategy = _db.Database.CreateExecutionStrategy();

        PersistedFormulaSet? result = null;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                // 1) Set mevcut mu?
                var exists = await _db.FormulaSets.AsNoTracking().AnyAsync(x => x.Id == set.Id, ct);
                if (!exists)
                    throw new DbUpdateConcurrencyException(
                        "FormulaSet güncellenemedi: Kayıt DB'de bulunamadı (0 row). Muhtemelen başka bir işlem tarafından silindi. (R17H)");

                var name = (set.Name ?? string.Empty).Trim();
                var scopeType = string.IsNullOrWhiteSpace(set.ScopeType) ? "Custom" : set.ScopeType.Trim();
                var selectedInputsJson = string.IsNullOrWhiteSpace(set.SelectedInputsJson) ? "[]" : set.SelectedInputsJson;

                // 2) Scalars (tek komut)
                var affectedSet = await _db.FormulaSets
                    .Where(x => x.Id == set.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Name, name)
                        .SetProperty(x => x.ScopeType, scopeType)
                        .SetProperty(x => x.IsActive, set.IsActive)
                        .SetProperty(x => x.SelectedInputsJson, selectedInputsJson)
                        .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow),
                        ct);

                if (affectedSet != 1)
                    throw new DbUpdateConcurrencyException(
                        $"FormulaSet güncellenemedi: beklenen 1 satır, etkilenen {affectedSet}. (R17H)");

                // 3) Child wipe (tek komut)
                await _db.FormulaLines
                    .Where(x => x.SetId == set.Id)
                    .ExecuteDeleteAsync(ct);

                // 4) Child reinsert (normalize + dedupe)
                static string N(string? s) => (s ?? string.Empty).Trim();

                var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var incoming = set.Lines ?? new List<PersistedFormulaLine>();
                var newLines = new List<PersistedFormulaLine>(incoming.Count);

                foreach (var l in incoming)
                {
                    var targetKey = N(l.TargetKey);
                    if (string.IsNullOrWhiteSpace(targetKey))
                        continue;

                    var mode = string.IsNullOrWhiteSpace(l.Mode) ? "Map" : N(l.Mode);
                    var sourceKey = string.IsNullOrWhiteSpace(l.SourceKey) ? null : N(l.SourceKey);
                    var expr = string.IsNullOrWhiteSpace(l.Expression) ? null : l.Expression!.Trim();

                    var sort = l.SortOrder;
                    var isHidden = l.IsHidden;

                    var sig = $"{targetKey}||{mode}||{sourceKey}||{expr}||{sort}||{isHidden}";
                    if (!dedup.Add(sig))
                        continue;

                    newLines.Add(new PersistedFormulaLine
                    {
                        Id = Guid.NewGuid(),
                        SetId = set.Id,
                        TargetKey = targetKey,
                        Mode = mode,
                        SourceKey = sourceKey,
                        Expression = expr,
                        SortOrder = sort,
                        IsHidden = isHidden
                    });
                }

                if (newLines.Count > 0)
                    await _db.FormulaLines.AddRangeAsync(newLines, ct);

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                // 5) Re-load: kullanıcı güncel hâli anında görsün
                result = await _db.FormulaSets
                    .AsNoTracking()
                    .Include(x => x.Lines)
                    .FirstAsync(x => x.Id == set.Id, ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await tx.RollbackAsync(ct);

                // Entry dump (KANIT): hangi entity/PK 0 row affected oldu?
                var dump = await BuildConcurrencyEntryDumpAsync(ex, ct);
                if (!string.IsNullOrWhiteSpace(dump))
                    ex.Data["EntryDump"] = dump;

                throw;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });

        return result!;
    }

    private async Task<string> BuildConcurrencyEntryDumpAsync(DbUpdateConcurrencyException ex, CancellationToken ct)
    {
        try
        {
            var sb = new System.Text.StringBuilder();

            foreach (var entry in ex.Entries)
            {
                sb.AppendLine($"Entry: {entry.Metadata.ClrType.Name} | State={entry.State}");

                // Primary Key
                var pk = entry.Metadata.FindPrimaryKey();
                if (pk is not null)
                {
                    foreach (var p in pk.Properties)
                    {
                        var v = entry.Property(p.Name).CurrentValue;
                        sb.AppendLine($"  PK {p.Name}={v}");
                    }
                }

                // Values (kısa)
                foreach (var p in entry.Properties)
                {
                    if (p.Metadata.IsPrimaryKey())
                        continue;

                    var cur = p.CurrentValue;
                    var orig = p.OriginalValue;
                    if (!Equals(cur, orig))
                        sb.AppendLine($"  Δ {p.Metadata.Name}: orig='{orig}' cur='{cur}'");
                }

                // DB snapshot
                var dbVals = await entry.GetDatabaseValuesAsync(ct);
                if (dbVals is null)
                {
                    sb.AppendLine("  DB: <row not found>");
                }
                else
                {
                    sb.AppendLine("  DB: <row exists>");
                }
            }

            return sb.ToString().Trim();
        }
        catch (Exception dumpEx)
        {
            // Concurrency dump oluşturma hatası — log edilmeli ama işlemi durdurmamalı
            System.Diagnostics.Debug.WriteLine($"[FormulaSetStore] BuildConcurrencyEntryDumpAsync hatası: {dumpEx.Message}");
            return string.Empty;
        }
    }


    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _db.FormulaSets
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (existing is null) return;

        _db.FormulaLines.RemoveRange(existing.Lines);
        _db.FormulaSets.Remove(existing);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(Guid id, CancellationToken ct = default)
    {
        var target = await _db.FormulaSets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (target is null) return;

        var scope = target.ScopeType;

        // Performans: Tüm scope set'lerini belleğe yüklemek yerine tek SQL komutu
        await _db.FormulaSets
            .Where(x => x.ScopeType == scope && x.Id != id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct);

        await _db.FormulaSets
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true), ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var target = await _db.FormulaSets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (target is null) return;

        target.IsActive = false;
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddRunAsync(Guid setId, string inputsJson, string outputsJson, string issuesJson, CancellationToken ct = default)
    {
        // Run logging opsiyonel.
        var setExists = await _db.FormulaSets.AnyAsync(x => x.Id == setId, ct);
        if (!setExists) return;

        _db.FormulaRuns.Add(new PersistedFormulaRun
        {
            Id = Guid.NewGuid(),
            SetId = setId,
            RunAtUtc = DateTime.UtcNow,
            InputsJson = inputsJson ?? "{}",
            OutputsJson = outputsJson ?? "{}",
            IssuesJson = issuesJson ?? "[]"
        });

        await _db.SaveChangesAsync(ct);
    }
}
