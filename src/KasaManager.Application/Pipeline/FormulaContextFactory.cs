#nullable enable
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.FormulaEngine.Authoring;

namespace KasaManager.Application.Pipeline;

/// <summary>
/// R20 Wave 3: Formula Context Factory implementation.
/// FormulaContext oluşturur ve pipeline ile doldurur.
/// </summary>
public sealed class FormulaContextFactory : IFormulaContextFactory
{
    private readonly IDataPipeline _pipeline;
    private readonly IFormulaSetStore _formulaSetStore;
    
    public FormulaContextFactory(
        IDataPipeline pipeline,
        IFormulaSetStore formulaSetStore)
    {
        _pipeline = pipeline;
        _formulaSetStore = formulaSetStore;
    }
    
    /// <summary>
    /// Yeni FormulaContext oluştur (veri pipeline ile doldurulmuş).
    /// </summary>
    public async Task<Result<FormulaContext>> CreateAsync(FormulaContextRequest request, CancellationToken ct = default)
    {
        // 1. Pipeline ile verileri yükle
        var pipelineRequest = new PipelineRequest
        {
            RaporTarihi = request.RaporTarihi,
            UploadFolder = request.UploadFolder,
            KasaScope = request.KasaScope,
            FullExcelTotals = false,
            UserInputs = request.UserInputs,
            RangeStart = request.RangeStart,
            RangeEnd = request.RangeEnd
        };
        
        var pipelineResult = await _pipeline.ExecuteAsync(pipelineRequest, ct);
        if (!pipelineResult.Ok)
            return Result<FormulaContext>.Fail(pipelineResult.Error ?? "Pipeline çalıştırma hatası");
        
        // 2. FormulaContext oluştur
        var context = new FormulaContext();
        
        // 3. Pipeline sonuçlarını context'e aktar
        foreach (var cell in pipelineResult.Value!.Cells.Values)
        {
            context.Cells.Set(cell);
        }
        
        // 4. FormulaSet varsa formülleri yükle
        if (request.FormulaSetId.HasValue)
        {
            var formulaSet = await _formulaSetStore.GetAsync(request.FormulaSetId.Value, ct);
            if (formulaSet is not null)
            {
                LoadFormulasFromSet(context, formulaSet);
            }
        }
        
        return Result<FormulaContext>.Success(context);
    }
    
    /// <summary>
    /// Mevcut FormulaSet'ten context oluştur.
    /// </summary>
    public async Task<Result<FormulaContext>> CreateFromFormulaSetAsync(
        Guid formulaSetId, 
        DateOnly raporTarihi, 
        string uploadFolder, 
        CancellationToken ct = default)
    {
        // FormulaSet'i yükle
        var formulaSet = await _formulaSetStore.GetAsync(formulaSetId, ct);
        if (formulaSet is null)
            return Result<FormulaContext>.Fail($"FormulaSet bulunamadı: {formulaSetId}");
        
        // Context oluştur
        var request = new FormulaContextRequest
        {
            RaporTarihi = raporTarihi,
            UploadFolder = uploadFolder,
            KasaScope = formulaSet.ScopeType ?? "Aksam",
            FormulaSetId = formulaSetId
        };
        
        return await CreateAsync(request, ct);
    }
    
    private static void LoadFormulasFromSet(FormulaContext context, PersistedFormulaSet formulaSet)
    {
        // SelectedInputsJson'dan seçili key'leri parse et
        if (!string.IsNullOrWhiteSpace(formulaSet.SelectedInputsJson))
        {
            try
            {
                var selectedKeys = JsonSerializer.Deserialize<List<string>>(formulaSet.SelectedInputsJson);
                if (selectedKeys is not null)
                {
                    foreach (var key in selectedKeys)
                    {
                        context.SelectCell(key);
                    }
                }
            }
            catch (Exception ex)
            {
                // P1-EXC-01: SelectedInputs JSON parse hatası — alan seçimi atlanacak
                System.Diagnostics.Debug.WriteLine($"[FormulaContextFactory] SelectedInputsJson parse hatası: {ex.Message}");
            }
        }
        
        // R23 FIX: Formüllerde kullanılan tüm değişkenleri otomatik seç
        var allFormulaVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Lines'ı formüllere dönüştür
        if (formulaSet.Lines is not null)
        {
            foreach (var line in formulaSet.Lines.Where(l => !l.IsHidden))
            {
                // Expression varsa formül olarak ekle
                if (!string.IsNullOrWhiteSpace(line.Expression))
                {
                    context.AddFormula(new FormulaDefinition
                    {
                        Id = Guid.NewGuid(),
                        TargetKey = line.TargetKey ?? "",
                        Expression = line.Expression,
                        DisplayName = line.TargetKey,
                        SortOrder = line.SortOrder
                    });
                    
                    // R23: Formüldeki değişkenleri topla
                    var vars = ExtractVariables(line.Expression);
                    foreach (var v in vars) allFormulaVariables.Add(v);
                }
                // SourceKey varsa basit mapping (a = b)
                else if (!string.IsNullOrWhiteSpace(line.SourceKey))
                {
                    context.AddFormula(new FormulaDefinition
                    {
                        Id = Guid.NewGuid(),
                        TargetKey = line.TargetKey ?? "",
                        Expression = line.SourceKey,
                        DisplayName = line.TargetKey,
                        SortOrder = line.SortOrder
                    });
                    
                    // R23: SourceKey'i de değişken olarak seç
                    allFormulaVariables.Add(line.SourceKey);
                }
            }
        }
        
        // R23: Tüm formül değişkenlerini otomatik seç
        foreach (var varName in allFormulaVariables)
        {
            context.SelectCell(varName);
        }
    }
    
    /// <summary>
    /// Formülden değişken isimlerini çıkar.
    /// </summary>
    private static HashSet<string> ExtractVariables(string expression)
    {
        var vars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(expression)) return vars;

        // quick identifier scan: underscore/letters/digits
        var m = System.Text.RegularExpressions.Regex.Matches(expression, @"\b[a-zA-Z_][a-zA-Z0-9_]*\b");
        foreach (System.Text.RegularExpressions.Match mm in m)
        {
            var s = mm.Value;
            // skip numbers
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _)) continue;
            vars.Add(s);
        }
        return vars;
    }
}
