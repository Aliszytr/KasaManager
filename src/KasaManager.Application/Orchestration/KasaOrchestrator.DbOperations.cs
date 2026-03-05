using System.Text.Json;
using KasaManager.Application.Orchestration.Dtos;
using KasaManager.Domain.FormulaEngine.Authoring;
using Microsoft.Extensions.Logging;

namespace KasaManager.Application.Orchestration;

// DB Operations: CRUD, persistence helpers, concurrency recovery
public partial class KasaOrchestrator
{
    // =========================================================================
    // DB Methods
    // =========================================================================

    public async Task HydrateDbFormulaSetsAsync(KasaPreviewDto dto, CancellationToken ct)
    {
        try
        {
            var sets = await _formulaSetStore.ListAsync(ct);
            dto.DbFormulaSets = sets
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Select(x => new FormulaSetListItem
                {
                    Id = x.Id.ToString(),
                    Name = x.Name,
                    ScopeType = x.ScopeType,
                    IsActive = x.IsActive,
                    UpdatedAtUtc = x.UpdatedAtUtc.ToString("yyyy-MM-dd HH:mm")
                })
                .ToList();
        }
        catch (Exception ex)
        {
            dto.Warnings.Add($"DB List Error: {ex.Message}");
        }
    }

    public async Task LoadDbFormulaSetIntoModelAsync(KasaPreviewDto dto, CancellationToken ct)
    {
        if(!Guid.TryParse(dto.DbFormulaSetId, out var id)) { dto.Errors.Add("Set ID invalid"); return; }
        var set = await _formulaSetStore.GetAsync(id, ct);
        if(set == null) { dto.Errors.Add("Set not found"); return; }

        dto.DbFormulaSetId = set.Id.ToString();
        dto.DbFormulaSetName = set.Name;
        dto.DbScopeType = set.ScopeType ?? "Custom";
        
        try {
            dto.SelectedInputKeys = JsonSerializer.Deserialize<List<string>>(set.SelectedInputsJson ?? "[]") ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LoadDbFormulaSetIntoModelAsync: SelectedInputsJson parse hatası (SetId={SetId})", set.Id);
            dto.SelectedInputKeys = new();
        }

        dto.Mappings = (set.Lines ?? new List<PersistedFormulaLine>())
            .OrderBy(x => x.SortOrder)
            .Select(x => new KasaPreviewMappingRow {
                RowId = x.Id.ToString(),
                TargetKey = x.TargetKey,
                Mode = x.Mode,
                SourceKey = x.SourceKey,
                Expression = x.Expression,
                IsHidden = x.IsHidden
            }).ToList();
    }

    public async Task CreateDbFormulaSetAsync(KasaPreviewDto dto, CancellationToken ct)
    {
        try {
            var set = BuildPersistedSetFromUi(dto, forceId: Guid.NewGuid(), resetLineIds: true);
            await _formulaSetStore.CreateAsync(set, ct);
            dto.DbFormulaSetId = set.Id.ToString();
            dto.DbFormulaSetName = set.Name;
        } catch(Exception ex) { dto.Errors.Add($"Create failed: {ex.Message}"); }
        await HydrateDbFormulaSetsAsync(dto, ct);
    }
    
    public async Task SaveDbFormulaSetAsync(KasaPreviewDto dto, bool isUpdate, CancellationToken ct)
    {
         try
        {
            if (isUpdate)
            {
                if(!Guid.TryParse(dto.DbFormulaSetId, out var id)) return;
                // ÖNEMLİ: Mevcut IsActive durumunu koru — güncelleme sırasında aktiflik kaybedilmesin
                var existing = await _formulaSetStore.GetAsync(id, ct);
                var preserveActive = existing?.IsActive ?? false;
                var set = BuildPersistedSetFromUi(dto, id, preserveIsActive: preserveActive);
                await _formulaSetStore.UpdateAsync(set, ct);
            }
            else
            {
                 if(Guid.TryParse(dto.DbFormulaSetId, out var existingId))
                 {
                     var exists = await _formulaSetStore.GetAsync(existingId, ct);
                     if(exists != null)
                     {
                         // Mevcut IsActive durumunu koru
                         var upSet = BuildPersistedSetFromUi(dto, existingId, false, preserveIsActive: exists.IsActive);
                         await _formulaSetStore.UpdateAsync(upSet, ct);
                         return;
                     }
                 }
                 var newSet = BuildPersistedSetFromUi(dto, forceId: Guid.NewGuid(), resetLineIds: true);
                 await _formulaSetStore.CreateAsync(newSet, ct);
                 dto.DbFormulaSetId = newSet.Id.ToString();
            }
        }
           catch (Exception ex)
        {
            if(ex.GetType().Name.Contains("Concurrency"))
            {
                 if (ex.Data.Contains("EntryDump") && ex.Data["EntryDump"] is string dump && !string.IsNullOrWhiteSpace(dump))
                    dto.Warnings.Add("R17H EntryDump (kanıt):\n" + dump);
                 else
                    dto.Warnings.Add("R17H: " + ex.Message);

                 var recovered = await TryRecoverAndRetrySaveAsync(dto, ct);
                 if (!recovered)
                    dto.Errors.Add("Kaydetme sırasında eşzamanlılık (concurrency) hatası oluştu ve sistem otomatik toparlayamadı; sayfayı yenileyip tekrar deneyin. (R17H)");
                 return;
            }

            dto.Errors.Add($"Kaydetme başarısız: {ex.Message}");
        }

        await HydrateDbFormulaSetsAsync(dto, ct);
    }

    public async Task DeleteDbFormulaSetAsync(KasaPreviewDto dto, CancellationToken ct)
    {
         if(Guid.TryParse(dto.DbFormulaSetId, out var id))
            await _formulaSetStore.DeleteAsync(id, ct);
         
         dto.DbFormulaSetId = null;
         await HydrateDbFormulaSetsAsync(dto, ct);
    }

    public async Task CopyDbFormulaSetAsync(KasaPreviewDto dto, CancellationToken ct)
    {
        if(Guid.TryParse(dto.DbFormulaSetId, out var id))
        {
            var set = await _formulaSetStore.GetAsync(id, ct);
            if(set != null) {
                set.Id = Guid.NewGuid();
                set.Name += " (Copy)";
                set.IsActive = false;
                set.CreatedAtUtc = DateTime.UtcNow;
                set.UpdatedAtUtc = DateTime.UtcNow;
                foreach(var l in set.Lines) { l.Id = Guid.NewGuid(); l.SetId = set.Id; }
                await _formulaSetStore.CreateAsync(set, ct);
                dto.DbFormulaSetId = set.Id.ToString();
            }
        }
        await HydrateDbFormulaSetsAsync(dto, ct);
    }

    public async Task ToggleActiveDbFormulaSetAsync(KasaPreviewDto dto, CancellationToken ct)
    {
        if(Guid.TryParse(dto.DbFormulaSetId, out var id))
            await _formulaSetStore.SetActiveAsync(id, ct);
        await HydrateDbFormulaSetsAsync(dto, ct);
    }

    // =========================================================================
    // Persistence Helpers
    // =========================================================================

    private PersistedFormulaSet BuildPersistedSetFromUi(KasaPreviewDto dto, Guid forceId, bool resetLineIds = false, bool preserveIsActive = false)
    {
        var id = forceId;
        var now = DateTime.UtcNow;
        return new PersistedFormulaSet
        {
            Id = id,
            Name = !string.IsNullOrWhiteSpace(dto.DbFormulaSetName) ? dto.DbFormulaSetName : $"Set {DateTime.UtcNow:MM-dd HH:mm}",
            ScopeType = dto.DbScopeType ?? "Custom",
            IsActive = preserveIsActive,
            SelectedInputsJson = JsonSerializer.Serialize(dto.SelectedInputKeys),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Lines = (dto.Mappings ?? new List<KasaPreviewMappingRow>()).Select((x, i) => new PersistedFormulaLine
            {
                Id = resetLineIds ? Guid.NewGuid() : (Guid.TryParse(x.RowId, out var rid) ? rid : Guid.NewGuid()),
                SetId = id,
                TargetKey = x.TargetKey ?? string.Empty,
                Mode = x.Mode ?? "Formula",
                SourceKey = x.SourceKey,
                Expression = x.Expression ?? string.Empty,
                SortOrder = i,
                IsHidden = x.IsHidden
            }).ToList()
        };
    }

    private async Task<bool> TryRecoverAndRetrySaveAsync(KasaPreviewDto dto, CancellationToken ct)
    {
        if(!Guid.TryParse(dto.DbFormulaSetId, out var id)) return false;
        var db = await _formulaSetStore.GetAsync(id, ct);
        if (db is null) return false;

        var incoming = BuildPersistedSetFromUi(dto, id, resetLineIds: false);
        var dbLines = (db.Lines ?? new List<PersistedFormulaLine>()).ToList();
        var byId = dbLines.ToDictionary(x => x.Id, x => x);
        var byComposite = dbLines
            .GroupBy(x => BuildLineCompositeKey(x.TargetKey, x.Mode, x.SourceKey))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var uiLine in incoming.Lines)
        {
            if (uiLine.Id != Guid.Empty && byId.ContainsKey(uiLine.Id)) continue;
            var comp = BuildLineCompositeKey(uiLine.TargetKey, uiLine.Mode, uiLine.SourceKey);
            if (byComposite.TryGetValue(comp, out var match)) uiLine.Id = match.Id;
        }

        try
        {
            await _formulaSetStore.UpdateAsync(incoming, ct);
            dto.Warnings.Add("R17H: Concurrency recovery successful.");
            return true;
        }
        catch(Exception ex)
        {
             if(ex.GetType().Name.Contains("Concurrency"))
             {
                try {
                    var copy = BuildPersistedSetFromUi(dto, forceId: Guid.NewGuid(), resetLineIds: true);
                    copy.Name = $"{copy.Name} (Recovered)";
                    await _formulaSetStore.CreateAsync(copy, ct);
                    dto.DbFormulaSetId = copy.Id.ToString();
                    dto.DbFormulaSetName = copy.Name;
                    dto.Warnings.Add("R17H: Saved as copy due to concurrency.");
                    return true;
                } catch (Exception recoveryEx) { System.Diagnostics.Debug.WriteLine($"[KasaOrchestrator] Concurrency recovery başarısız: {recoveryEx.Message}"); return false; }
             }
             return false;
        }
    }

    private static string BuildLineCompositeKey(string? targetKey, string? mode, string? sourceKey)
    {
        var t = (targetKey ?? string.Empty).Trim();
        var m = (mode ?? "Map").Trim();
        var s = (sourceKey ?? string.Empty).Trim();
        return $"{t}||{m}||{s}";
    }
}
