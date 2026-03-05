#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Application.Pipeline; // R21: Unified CellRegistry
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace KasaManager.Web.Controllers;

/// <summary>
/// R17: Field Chooser API endpoints.
/// Alan tercihlerini yönetmek için kullanılır.
/// R21: Tüm alanlar artık CellRegistry'den geliyor.
/// </summary>
[Route("[controller]/[action]")]
[Authorize]
public sealed class FieldChooserController : Controller
{
    private readonly IFieldPreferenceService _fieldPreferenceService;
    private readonly IDataPipeline _dataPipeline; // R21: Unified CellRegistry
    private readonly ILogger<FieldChooserController> _logger;

    public FieldChooserController(
        IFieldPreferenceService fieldPreferenceService,
        IDataPipeline dataPipeline, // R21
        ILogger<FieldChooserController> logger)
    {
        _fieldPreferenceService = fieldPreferenceService;
        _dataPipeline = dataPipeline; // R21
        _logger = logger;
    }

    /// <summary>Field Chooser panelini partial olarak döndürür</summary>
    /// <param name="kasaType">Kasa türü: Aksam, Sabah, Genel</param>
    /// <param name="selectedKeys">Sol menüde (SelectedInputKeys) seçili alanlar - virgülle ayrılmış</param>
    /// <param name="poolKeys">R17B: Sol menüdeki tüm Pool alanları (HAM veri key'leri) - Alan Seçici'de gösterilmesi için</param>
    /// <param name="loadedTemplateName">R18: Yüklenen şablon adı (DB'den yüklenmiş ise)</param>
    [HttpGet]
    public async Task<IActionResult> GetPanel(string kasaType, string? selectedKeys, string? poolKeys, string? loadedTemplateName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(kasaType))
            kasaType = "Aksam";

        // Sol menüden gelen seçili alanları parse et
        var currentlySelected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(selectedKeys))
        {
            foreach (var key in selectedKeys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                currentlySelected.Add(key);
            }
        }

        // R17B: Pool key'lerini parse et - Sol Menü ile senkronizasyon için
        var poolKeySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(poolKeys))
        {
            foreach (var key in poolKeys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                poolKeySet.Add(key);
            }
        }

        var model = await FieldChooserViewModel.CreateWithSelectedAndPoolAsync(
            _fieldPreferenceService,
            kasaType,
            currentlySelected,
            poolKeySet,
            loadedTemplateName, // R18: Yüklenen şablon adı
            ct);

        return PartialView("_FieldChooserPanel", model);
    }


    /// <summary>Seçimleri kaydet</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePreferences([FromBody] SavePreferencesRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.KasaType))
        {
            return BadRequest(new { success = false, message = "Geçersiz istek" });
        }

        try
        {
            await _fieldPreferenceService.SaveSelectedFieldsAsync(
                request.KasaType,
                User?.Identity?.Name,
                request.SelectedFields ?? new List<string>(),
                ct);

            _logger.LogInformation("R17: Alan tercihleri kaydedildi - KasaType={KasaType}, User={User}, Fields={Count}",
                request.KasaType, User?.Identity?.Name ?? "(anonymous)", request.SelectedFields?.Count ?? 0);

            return Ok(new { success = true, message = "Alan tercihleri kaydedildi." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "R17: Alan tercihleri kaydedilirken hata");
            return StatusCode(500, new { success = false, message = "Kayıt sırasında hata oluştu." });
        }
    }

    /// <summary>Varsayılana dön</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetToDefaults(string kasaType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(kasaType))
            kasaType = "Aksam";

        try
        {
            await _fieldPreferenceService.ResetToDefaultsAsync(kasaType, User?.Identity?.Name, ct);
            var defaultFields = _fieldPreferenceService.GetDefaultFieldsFor(kasaType);

            return Ok(new { success = true, defaultFields, message = "Varsayılana dönüldü." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "R17: Varsayılana dönüş hatası");
            return StatusCode(500, new { success = false, message = "İşlem sırasında hata oluştu." });
        }
    }

    /// <summary>Seçili alanları getir (JSON)</summary>
    [HttpGet]
    public async Task<IActionResult> GetSelectedFields(string kasaType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(kasaType))
            kasaType = "Aksam";

        var fields = await _fieldPreferenceService.GetSelectedFieldsAsync(kasaType, User?.Identity?.Name, ct);
        return Ok(new { kasaType, selectedFields = fields });
    }

    /// <summary>Tüm alanlar kataloğunu getir (UI için)</summary>
    [HttpGet]
    public IActionResult GetCatalog()
    {
        var groups = _fieldPreferenceService.GetFieldCatalogGrouped();
        return Ok(groups.Select(g => new 
        { 
            category = g.Category, 
            fields = g.Fields.Select(f => new 
            {
                key = f.Key,
                displayName = f.DisplayName,
                description = f.Description,
                category = f.Category,
                isReadOnly = f.IsReadOnly,
                icon = f.Icon,
                colorClass = f.ColorClass
            })
        }));
    }
    
    /// <summary>
    /// R21: CellRegistry'den tüm alanları getir.
    /// Bu endpoint, R16 sol menüsünün TÜM alanları göstermesini sağlar.
    /// Seçili alanlar FormulaSet ile birlikte kaydedilir.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetUnifiedFields(
        DateOnly? date, 
        string? kasaType, 
        string? selectedKeys, 
        Guid? formulaSetId,
        CancellationToken ct)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var scope = kasaType ?? "Aksam";
        
        // Seçili key'leri parse et
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(selectedKeys))
        {
            foreach (var key in selectedKeys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                selected.Add(key);
            }
        }
        
        try
        {
            // R21: CellRegistry'den tüm alanları al
            var uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", targetDate.ToString("yyyy-MM-dd"));
            
            var request = new PipelineRequest
            {
                RaporTarihi = targetDate,
                UploadFolder = uploadFolder,
                KasaScope = scope,

                // Sol menü "alanları getir" için True Source (tüm Excel toplamları) gerekmez.
                // Bu ayar performansı dramatik şekilde etkiler.
                FullExcelTotals = false
            };
            
            var result = await _dataPipeline.ExecuteAsync(request, ct);
            
            if (!result.Ok || result.Value is null)
            {
                return StatusCode(500, new { success = false, message = result.Error ?? "Pipeline hatası" });
            }
            
            var cells = result.Value.Cells.Values.ToList();
            
            // Kategori bazlı grupla
            var groups = cells
                .GroupBy(c => c.Category ?? "Diğer")
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    category = g.Key,
                    icon = GetCategoryIcon(g.Key),
                    colorClass = GetCategoryColor(g.Key),
                    fields = g.OrderBy(c => c.DisplayName).Select(c => new
                    {
                        key = c.Key,
                        displayName = c.DisplayName ?? c.Key,
                        value = c.Value,
                        source = c.Source.ToString(),
                        isSelected = selected.Contains(c.Key)
                    }).ToList()
                })
                .ToList();
            
            _logger.LogInformation("R21: GetUnifiedFields - Date={Date}, Scope={Scope}, TotalCells={Count}", 
                targetDate, scope, cells.Count);
            
            return Ok(new
            {
                success = true,
                date = targetDate.ToString("yyyy-MM-dd"),
                scope,
                totalCells = cells.Count,
                selectedCount = selected.Count,
                groups
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "R21: GetUnifiedFields hatası");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }
    
    private static string GetCategoryIcon(string category) => category switch
    {
        "Excel" or "HAM" => "bi-file-earmark-excel",
        "Kullanıcı Girişi" or "UserInput" => "bi-pencil-square",
        "Ayarlar" or "Settings" => "bi-gear",
        "Hesaplanan" or "Calculated" => "bi-calculator",
        "Ortak Kasa" => "bi-share",
        _ => "bi-box"
    };
    
    private static string GetCategoryColor(string category) => category switch
    {
        "Excel" or "HAM" => "text-success",
        "Kullanıcı Girişi" or "UserInput" => "text-primary",
        "Ayarlar" or "Settings" => "text-warning",
        "Hesaplanan" or "Calculated" => "text-info",
        _ => "text-secondary"
    };
}

/// <summary>Tercih kaydetme isteği</summary>
public sealed class SavePreferencesRequest
{
    public string KasaType { get; set; } = string.Empty;
    public List<string>? SelectedFields { get; set; }
}
