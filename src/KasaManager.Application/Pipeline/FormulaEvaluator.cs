#nullable enable
using System.Data;
using System.Text.RegularExpressions;

namespace KasaManager.Application.Pipeline;

/// <summary>
/// R20 Wave 2: Formula Evaluator - formül hesaplama motoru.
/// System.Data.DataTable.Compute kullanarak expression evaluation yapar.
/// Harici bağımlılık gerektirmez.
/// </summary>
public sealed partial class FormulaEvaluator
{
    private readonly CellRegistry _cells;
    private readonly FormulaRegistry _formulas;
    
    public FormulaEvaluator(CellRegistry cells, FormulaRegistry formulas)
    {
        _cells = cells;
        _formulas = formulas;
    }
    
    /// <summary>
    /// Tüm formülleri sırayla hesapla ve sonuçları CellRegistry'ye yaz.
    /// </summary>
    public EvaluationResult EvaluateAll()
    {
        var startTime = DateTime.UtcNow;
        var errors = new List<EvaluationError>();
        var computed = new Dictionary<string, decimal>();
        var debugLog = new List<string>();
        
        // Topooljik sırayla formülleri al
        var sortedFormulas = _formulas.GetSortedForEvaluation();
        debugLog.Add($"[Evaluator] {sortedFormulas.Count} formül hesaplanacak");
        
        foreach (var formula in sortedFormulas)
        {
            try
            {
                var result = EvaluateSingle(formula, debugLog);
                computed[formula.TargetKey] = result;
                
                // Sonucu CellRegistry'ye yaz (Derived olarak)
                _cells.Set(new Cell
                {
                    Key = formula.TargetKey,
                    Value = result,
                    Source = CellSource.Derived,
                    DisplayName = formula.DisplayName ?? formula.TargetKey,
                    Notes = $"Formül: {formula.Expression}"
                });
                
                debugLog.Add($"[Evaluator] {formula.TargetKey} = {result:N2}");
            }
            catch (Exception ex)
            {
                errors.Add(new EvaluationError(formula.Id, formula.TargetKey, ex.Message));
                debugLog.Add($"[Evaluator] HATA: {formula.TargetKey} - {ex.Message}");
            }
        }
        
        var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
        debugLog.Add($"[Evaluator] Tamamlandı: {computed.Count} sonuç, {errors.Count} hata, {elapsed:N0}ms");
        
        return new EvaluationResult
        {
            ComputedValues = computed,
            Errors = errors,
            ExecutionTimeMs = (long)elapsed,
            DebugLog = debugLog
        };
    }
    
    /// <summary>
    /// Tek bir formülü hesapla.
    /// </summary>
    public decimal EvaluateSingle(FormulaDefinition formula, List<string>? debugLog = null)
    {
        if (string.IsNullOrWhiteSpace(formula.Expression))
            return 0m;
        
        // Expression'ı DataTable formatına dönüştür
        var expression = PrepareExpression(formula.Expression);
        debugLog?.Add($"[Expr] {formula.TargetKey}: {expression}");
        
        // DataTable.Compute ile hesapla
        using var table = new DataTable();
        var result = table.Compute(expression, null);
        
        return result switch
        {
            double d => (decimal)d,
            int i => i,
            decimal dec => dec,
            float f => (decimal)f,
            long l => l,
            _ => 0m
        };
    }
    
    /// <summary>
    /// Expression'ı DataTable.Compute formatına hazırla.
    /// </summary>
    private string PrepareExpression(string expression)
    {
        // Identifier'ları değerleriyle değiştir
        var result = IdentifierRegex().Replace(expression, match =>
        {
            var id = match.Value;
            // Built-in fonksiyonları atla
            if (IsBuiltInFunction(id)) return id;
            
            // Cell değerini al
            var value = _cells.GetValue(id, 0m);
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        });
        
        // max(a, b) -> IIF(a > b, a, b)
        result = MaxFunctionRegex().Replace(result, "IIF($1 > $2, $1, $2)");
        
        // min(a, b) -> IIF(a < b, a, b)
        result = MinFunctionRegex().Replace(result, "IIF($1 < $2, $1, $2)");
        
        // abs(a) -> IIF(a < 0, -a, a)
        result = AbsFunctionRegex().Replace(result, "IIF($1 < 0, -($1), $1)");
        
        return result;
    }
    
    private static bool IsBuiltInFunction(string id)
    {
        return id.Equals("IIF", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("max", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("min", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("abs", StringComparison.OrdinalIgnoreCase);
    }
    
    [GeneratedRegex(@"[a-zA-Z_][a-zA-Z0-9_]*", RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();
    
    [GeneratedRegex(@"max\s*\(\s*([^,]+)\s*,\s*([^)]+)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MaxFunctionRegex();
    
    [GeneratedRegex(@"min\s*\(\s*([^,]+)\s*,\s*([^)]+)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MinFunctionRegex();
    
    [GeneratedRegex(@"abs\s*\(\s*([^)]+)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AbsFunctionRegex();
}

/// <summary>
/// Formül hesaplama sonucu.
/// </summary>
public sealed class EvaluationResult
{
    public IReadOnlyDictionary<string, decimal> ComputedValues { get; init; } = new Dictionary<string, decimal>();
    public IReadOnlyList<EvaluationError> Errors { get; init; } = Array.Empty<EvaluationError>();
    public long ExecutionTimeMs { get; init; }
    public IReadOnlyList<string> DebugLog { get; init; } = Array.Empty<string>();
    
    public bool HasErrors => Errors.Count > 0;
    public bool IsSuccess => !HasErrors;
}

/// <summary>
/// Formül hesaplama hatası.
/// </summary>
public sealed record EvaluationError(Guid FormulaId, string TargetKey, string Message);
