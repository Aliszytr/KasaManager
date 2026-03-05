using System.Text.Json;
using KasaManager.Application.Orchestration;
using KasaManager.Application.Orchestration.Dtos;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace KasaManager.Web.Controllers;

/// <summary>
/// R16 Formula Designer — Profesyonel formül yönetim ekranı.
/// Şablon CRUD, pool veri önizleme, canlı FormulaEngine test.
/// </summary>
[Authorize(Roles = "Admin")]
public class FormulaDesignerController : Controller
{
    private readonly IKasaOrchestrator _orchestrator;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _cfg;

    public FormulaDesignerController(
        IKasaOrchestrator orchestrator,
        IWebHostEnvironment env,
        IConfiguration cfg)
    {
        _orchestrator = orchestrator;
        _env = env;
        _cfg = cfg;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = new FormulaDesignerViewModel();
        await HydrateTemplateList(model, ct);
        return View(model);
    }

    // ── LOAD ─────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoadTemplate(FormulaDesignerViewModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.SelectedTemplateId))
        {
            model.Errors.Add("Lütfen bir şablon seçin.");
            await HydrateTemplateList(model, ct);
            return View("Index", model);
        }

        var dto = new KasaPreviewDto { DbFormulaSetId = model.SelectedTemplateId };
        await _orchestrator.LoadDbFormulaSetIntoModelAsync(dto, ct);

        if (dto.Errors.Count > 0)
        {
            model.Errors.AddRange(dto.Errors);
            await HydrateTemplateList(model, ct);
            return View("Index", model);
        }

        // R16 FIX: ModelState temizle — asp-for tag helper'ları ModelState değerlerini
        // model değerlerinin üzerine yazar. DB'den gelen TemplateName, ScopeType vs.
        // doğru görüntülensin diye ModelState temizlenmeli.
        ModelState.Clear();

        model.TemplateName = dto.DbFormulaSetName ?? "";
        model.ScopeType = dto.DbScopeType ?? "Custom";
        model.Rows = (dto.Mappings ?? new())
            .Select((m, i) => new FormulaDesignerRow
            {
                Index = i,
                TargetKey = m.TargetKey ?? "",
                Mode = m.Mode ?? "Map",
                SourceKey = m.SourceKey ?? "",
                Expression = m.Expression ?? "",
                IsHidden = m.IsHidden,
                RowId = m.RowId ?? $"row-{i}"
            }).ToList();

        model.Infos.Add($"'{model.TemplateName}' ({model.ScopeType}) şablonu yüklendi — {model.Rows.Count} satır.");
        await HydrateTemplateList(model, ct);
        await LoadPoolData(model, ct);
        return View("Index", model);
    }

    // ── SAVE NEW ─────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTemplate(FormulaDesignerViewModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.TemplateName))
        {
            model.Errors.Add("Şablon adı boş olamaz.");
            await HydrateTemplateList(model, ct);
            return View("Index", model);
        }

        ModelState.Clear();
        var dto = BuildDtoFromModel(model);

        try
        {
            // Create new template
            dto.DbFormulaSetId = null; // force create
            await _orchestrator.CreateDbFormulaSetAsync(dto, ct);
            model.SelectedTemplateId = dto.DbFormulaSetId;
            model.Infos.Add($"'{model.TemplateName}' yeni şablon oluşturuldu.");
        }
        catch (Exception ex)
        {
            model.Errors.Add($"Kaydetme hatası: {ex.Message}");
        }

        if (dto.Errors.Count > 0) model.Errors.AddRange(dto.Errors);
        if (dto.Warnings.Count > 0) model.Infos.AddRange(dto.Warnings);

        await HydrateTemplateList(model, ct);
        return View("Index", model);
    }

    // ── UPDATE EXISTING ──────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTemplate(FormulaDesignerViewModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.SelectedTemplateId))
        {
            model.Errors.Add("Güncellenecek şablon seçilmedi. Önce bir şablon yükleyin.");
            await HydrateTemplateList(model, ct);
            return View("Index", model);
        }

        if (string.IsNullOrWhiteSpace(model.TemplateName))
        {
            model.Errors.Add("Şablon adı boş olamaz.");
            await HydrateTemplateList(model, ct);
            return View("Index", model);
        }

        ModelState.Clear();
        var dto = BuildDtoFromModel(model);

        try
        {
            await _orchestrator.SaveDbFormulaSetAsync(dto, isUpdate: true, ct);
            model.Infos.Add($"'{model.TemplateName}' güncellendi.");
        }
        catch (Exception ex)
        {
            model.Errors.Add($"Güncelleme hatası: {ex.Message}");
        }

        if (dto.Errors.Count > 0) model.Errors.AddRange(dto.Errors);
        if (dto.Warnings.Count > 0) model.Infos.AddRange(dto.Warnings);

        await HydrateTemplateList(model, ct);
        return View("Index", model);
    }

    // ── DELETE ────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTemplate(FormulaDesignerViewModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.SelectedTemplateId))
        {
            model.Errors.Add("Silinecek şablon seçilmedi.");
            await HydrateTemplateList(model, ct);
            return View("Index", model);
        }

        ModelState.Clear();
        try
        {
            var dto = new KasaPreviewDto { DbFormulaSetId = model.SelectedTemplateId };
            await _orchestrator.DeleteDbFormulaSetAsync(dto, ct);

            if (dto.Errors.Count > 0)
                model.Errors.AddRange(dto.Errors);
            else
            {
                model.Infos.Add("Şablon başarıyla silindi.");
                model.SelectedTemplateId = null;
                model.TemplateName = "";
                model.Rows.Clear();
                model.PoolEntries.Clear();
            }
        }
        catch (Exception ex)
        {
            model.Errors.Add($"Silme hatası: {ex.Message}");
        }

        await HydrateTemplateList(model, ct);
        return View("Index", model);
    }

    // ── COPY ─────────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CopyTemplate(FormulaDesignerViewModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.SelectedTemplateId))
        {
            model.Errors.Add("Kopyalanacak şablon seçilmedi.");
            await HydrateTemplateList(model, ct);
            return View("Index", model);
        }

        ModelState.Clear();
        try
        {
            var dto = new KasaPreviewDto { DbFormulaSetId = model.SelectedTemplateId };
            await _orchestrator.CopyDbFormulaSetAsync(dto, ct);

            if (dto.Errors.Count > 0)
                model.Errors.AddRange(dto.Errors);
            else
            {
                model.SelectedTemplateId = dto.DbFormulaSetId;
                model.Infos.Add("Şablon kopyalandı.");
            }
        }
        catch (Exception ex)
        {
            model.Errors.Add($"Kopyalama hatası: {ex.Message}");
        }

        await HydrateTemplateList(model, ct);
        return View("Index", model);
    }

    // ── TOGGLE ACTIVE ────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(FormulaDesignerViewModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.SelectedTemplateId))
        {
            model.Errors.Add("Aktif/pasif yapılacak şablon seçilmedi.");
            await HydrateTemplateList(model, ct);
            return View("Index", model);
        }

        ModelState.Clear();
        try
        {
            var dto = new KasaPreviewDto { DbFormulaSetId = model.SelectedTemplateId };
            await _orchestrator.ToggleActiveDbFormulaSetAsync(dto, ct);

            if (dto.Errors.Count > 0)
                model.Errors.AddRange(dto.Errors);
            else
                model.Infos.Add("Şablon aktiflik durumu değiştirildi.");
        }
        catch (Exception ex)
        {
            model.Errors.Add($"Aktifleştirme hatası: {ex.Message}");
        }

        await HydrateTemplateList(model, ct);
        return View("Index", model);
    }

    // ── TEST RUN ─────────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestRun(FormulaDesignerViewModel model, CancellationToken ct)
    {
        var dto = BuildDtoFromModel(model);
        var uploadPath = ResolveUploadFolderAbsolute();
        dto.SelectedDate = model.TestDate ?? DateOnly.FromDateTime(DateTime.Today);
        dto.IsDataLoaded = true;

        if (!string.IsNullOrWhiteSpace(model.ScopeType))
            dto.KasaType = model.ScopeType;

        // ── Kullanıcı girişlerini DTO'ya aktar ──
        dto.BozukPara = model.BozukPara;
        dto.NakitPara = model.NakitPara;
        dto.VergidenGelen = model.VergidenGelen;
        dto.GelmeyenD = model.GelmeyenD;
        dto.KasadaKalacakHedef = model.KasadaKalacakHedef;
        dto.KaydenTahsilat = model.KaydenTahsilat;
        dto.KaydenHarc = model.KaydenHarc;
        dto.BankadanCekilen = model.BankadanCekilen;
        dto.CesitliNedenlerleBankadanCikamayanTahsilat = model.CesitliNedenlerleBankadanCikamayanTahsilat;
        dto.BankayaGonderilmisDeger = model.BankayaGonderilmisDeger;
        dto.BankayaYatirilacakHarciDegistir = model.BankayaYatirilacakHarciDegistir;
        dto.BankayaYatirilacakTahsilatiDegistir = model.BankayaYatirilacakTahsilatiDegistir;

        await _orchestrator.RunFormulaEnginePreviewAsync(dto, uploadPath, ct);

        if (dto.FormulaRun?.Outputs != null)
        {
            model.TestResults = dto.FormulaRun.Outputs.ToDictionary(
                kv => kv.Key, kv => kv.Value);
            model.TestExplain = dto.FormulaRun.Explain?
                .Select(e => new FormulaExplainItem
                {
                    TargetKey = e.TargetKey,
                    Expression = e.Expression,
                    Result = e.Result
                }).ToList() ?? new();
        }

        if (dto.PoolEntries?.Count > 0)
        {
            model.PoolEntries = dto.PoolEntries.Select(p => new PoolEntryDisplay
            {
                Key = p.CanonicalKey,
                Value = p.Value ?? "—",
                Source = p.SourceName ?? "?",
                Type = p.Type.ToString()
            }).ToList();
        }

        if (dto.Errors.Count > 0) model.Errors.AddRange(dto.Errors);
        if (dto.Warnings.Count > 0) model.Infos.AddRange(dto.Warnings);

        model.HasTestResults = true;
        model.ShowUserInputs = true; // keep panel open after test
        await HydrateTemplateList(model, ct);
        return View("Index", model);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private KasaPreviewDto BuildDtoFromModel(FormulaDesignerViewModel model)
    {
        return new KasaPreviewDto
        {
            DbFormulaSetId = model.SelectedTemplateId,
            DbFormulaSetName = model.TemplateName,
            DbScopeType = model.ScopeType ?? "Custom",
            KasaType = model.ScopeType ?? "Custom",
            SelectedDate = model.TestDate ?? DateOnly.FromDateTime(DateTime.Today),
            Mappings = (model.Rows ?? new())
                .Select(r => new KasaManager.Application.Orchestration.Dtos.KasaPreviewMappingRow
                {
                    RowId = r.RowId,
                    TargetKey = r.TargetKey,
                    Mode = r.Mode,
                    SourceKey = r.SourceKey,
                    Expression = r.Expression,
                    IsHidden = r.IsHidden
                }).ToList()
        };
    }

    private async Task HydrateTemplateList(FormulaDesignerViewModel model, CancellationToken ct)
    {
        var dto = new KasaPreviewDto();
        await _orchestrator.HydrateDbFormulaSetsAsync(dto, ct);
        model.Templates = dto.DbFormulaSets?.Select(s => new TemplateListItem
        {
            Id = s.Id?.ToString() ?? "",
            Name = s.Name ?? "?",
            ScopeType = s.ScopeType ?? "Custom",
            IsActive = s.IsActive
        }).ToList() ?? new();
    }

    private async Task LoadPoolData(FormulaDesignerViewModel model, CancellationToken ct)
    {
        try
        {
            var dto = new KasaPreviewDto
            {
                SelectedDate = model.TestDate ?? DateOnly.FromDateTime(DateTime.Today),
                KasaType = model.ScopeType ?? "Aksam"
            };
            var uploadPath = ResolveUploadFolderAbsolute();
            await _orchestrator.LoadPreviewAsync(dto, uploadPath, ct);

            if (dto.PoolEntries?.Count > 0)
            {
                model.PoolEntries = dto.PoolEntries.Select(p => new PoolEntryDisplay
                {
                    Key = p.CanonicalKey,
                    Value = p.Value ?? "—",
                    Source = p.SourceName ?? "?",
                    Type = p.Type.ToString()
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            model.Infos.Add($"Pool veri yüklenemedi: {ex.Message}");
        }
    }

    private string ResolveUploadFolderAbsolute()
    {
        var sub = _cfg.GetValue<string>("Upload:SubFolder") ?? "Data\\Raporlar";
        sub = sub.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_env.WebRootPath, sub);
    }
}
