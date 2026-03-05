#nullable enable
using KasaManager.Domain.Abstractions;

namespace KasaManager.Application.Pipeline;

/// <summary>
/// R20 Wave 2: Formula Context - merkezi koordinatör.
/// Excel benzeri formül motoru için tek veri kaynağı ve hesaplama merkezi.
/// CellRegistry + FormulaRegistry + Evaluator birleşimini yönetir.
/// </summary>
public sealed class FormulaContext
{
    /// <summary>Tüm veri hücreleri.</summary>
    public CellRegistry Cells { get; }
    
    /// <summary>Tüm formül tanımları.</summary>
    public FormulaRegistry Formulas { get; }
    
    /// <summary>Son hesaplama sonucu.</summary>
    public EvaluationResult? LastEvaluationResult { get; private set; }
    
    /// <summary>Context ID (session tracking için).</summary>
    public Guid ContextId { get; } = Guid.NewGuid();
    
    /// <summary>Oluşturma zamanı.</summary>
    public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;
    
    // === Events ===
    
    /// <summary>Hesaplama tamamlandığında.</summary>
    public event Action<EvaluationResult>? Evaluated;
    
    /// <summary>Validation hatası oluştuğunda.</summary>
    public event Action<ValidationResult>? ValidationFailed;
    
    // === Constructor ===
    
    public FormulaContext()
    {
        Cells = new CellRegistry();
        Formulas = new FormulaRegistry();
        
        // Formül eklendiğinde otomatik bağımlılık takibi
        Formulas.FormulaChanged += OnFormulaChanged;
        Formulas.FormulaRemoved += OnFormulaRemoved;
    }
    
    public FormulaContext(CellRegistry cells, FormulaRegistry formulas)
    {
        Cells = cells;
        Formulas = formulas;
        
        Formulas.FormulaChanged += OnFormulaChanged;
        Formulas.FormulaRemoved += OnFormulaRemoved;
    }
    
    // === Cell Operations (delegated) ===
    
    /// <summary>Hücre değeri ayarla.</summary>
    public void SetCellValue(string key, decimal value)
    {
        var existing = Cells.Get(key);
        if (existing is not null)
        {
            Cells.Set(existing with { Value = value });
        }
        else
        {
            Cells.Set(new Cell
            {
                Key = key,
                Value = value,
                Source = CellSource.UserInput
            });
        }
    }
    
    /// <summary>Hücre seç (sol menüde görünür yap).</summary>
    public void SelectCell(string key)
    {
        Cells.Select(key);
    }
    
    /// <summary>
    /// Hücre seçimini kaldır (formül kontrolü ile).
    /// Formülde kullanılıyorsa uyarı döner.
    /// </summary>
    public (bool success, string? warning) TryDeselectCell(string key)
    {
        // Önce formüllerde kullanılıyor mu kontrol et
        var usingFormulas = Formulas.GetFormulasUsingKey(key);
        if (usingFormulas.Count > 0)
        {
            var formulaNames = string.Join(", ", usingFormulas.Select(f => f.DisplayName ?? f.TargetKey));
            return (false, $"'{key}' şu formüllerde kullanılıyor: {formulaNames}. Önce formüllerden kaldırın.");
        }
        
        return Cells.TryDeselect(key);
    }
    
    // === Formula Operations (delegated) ===
    
    /// <summary>Formül ekle.</summary>
    public void AddFormula(FormulaDefinition formula)
    {
        Formulas.Set(formula);
    }
    
    /// <summary>Formül sil.</summary>
    public bool RemoveFormula(Guid formulaId)
    {
        return Formulas.Remove(formulaId);
    }
    
    /// <summary>Formül gizle (soft delete).</summary>
    public bool HideFormula(Guid formulaId)
    {
        return Formulas.Hide(formulaId);
    }
    
    // === Evaluation ===
    
    /// <summary>Tüm formülleri hesapla.</summary>
    public EvaluationResult Evaluate()
    {
        var evaluator = new FormulaEvaluator(Cells, Formulas);
        LastEvaluationResult = evaluator.EvaluateAll();
        Evaluated?.Invoke(LastEvaluationResult);
        return LastEvaluationResult;
    }
    
    /// <summary>Tek formül hesapla (preview için).</summary>
    public decimal EvaluateFormula(FormulaDefinition formula)
    {
        var evaluator = new FormulaEvaluator(Cells, Formulas);
        return evaluator.EvaluateSingle(formula);
    }
    
    // === Validation ===
    
    /// <summary>Context'i validate et.</summary>
    public ValidationResult Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        
        // 1. Formül bağımlılıkları kontrol et
        foreach (var formula in Formulas.GetVisible())
        {
            foreach (var dep in formula.Dependencies)
            {
                if (!Cells.ContainsKey(dep) && !Formulas.HasFormulaFor(dep))
                {
                    errors.Add($"Formül '{formula.TargetKey}': Bağımlılık '{dep}' bulunamadı.");
                }
            }
        }
        
        // 2. Döngüsel bağımlılık kontrol et
        var circularDeps = DetectCircularDependencies();
        if (circularDeps.Count > 0)
        {
            foreach (var cycle in circularDeps)
            {
                errors.Add($"Döngüsel bağımlılık tespit edildi: {string.Join(" -> ", cycle)}");
            }
        }
        
        // 3. Seçili hücrelerin formül bağımlılıkları
        foreach (var cell in Cells.GetSelected())
        {
            if (cell.UsedByFormulas.Count == 0)
            {
                warnings.Add($"'{cell.Key}' seçili ama hiçbir formülde kullanılmıyor.");
            }
        }
        
        var result = new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
        
        if (!result.IsValid)
            ValidationFailed?.Invoke(result);
        
        return result;
    }
    
    // === Private Helpers ===
    
    private void OnFormulaChanged(FormulaDefinition formula)
    {
        // Yeni bağımlılıkları Cell'lere kaydet
        foreach (var dep in formula.Dependencies)
        {
            Cells.AddFormulaReference(dep, formula.Id);
        }
    }
    
    private void OnFormulaRemoved(FormulaDefinition formula)
    {
        // Bağımlılık referanslarını temizle
        foreach (var dep in formula.Dependencies)
        {
            Cells.RemoveFormulaReference(dep, formula.Id);
        }
    }
    
    private List<List<string>> DetectCircularDependencies()
    {
        var cycles = new List<List<string>>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recursionStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();
        
        void DFS(string key)
        {
            if (recursionStack.Contains(key))
            {
                // Döngü bulundu
                var cycleStart = path.IndexOf(key);
                if (cycleStart >= 0)
                {
                    cycles.Add(path.Skip(cycleStart).ToList());
                }
                return;
            }
            
            if (visited.Contains(key)) return;
            
            visited.Add(key);
            recursionStack.Add(key);
            path.Add(key);
            
            var formula = Formulas.GetByTargetKey(key);
            if (formula is not null)
            {
                foreach (var dep in formula.Dependencies)
                {
                    DFS(dep);
                }
            }
            
            path.RemoveAt(path.Count - 1);
            recursionStack.Remove(key);
        }
        
        foreach (var formula in Formulas.GetVisible())
        {
            DFS(formula.TargetKey);
        }
        
        return cycles;
    }
}

/// <summary>
/// Validation sonucu.
/// </summary>
public sealed class ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
