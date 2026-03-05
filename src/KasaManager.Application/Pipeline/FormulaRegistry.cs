#nullable enable
using System.Collections;

namespace KasaManager.Application.Pipeline;

/// <summary>
/// R20 Wave 2: Formula Registry - formül deposu.
/// Tüm formülleri tutar ve bağımlılık takibi yapar.
/// </summary>
public sealed class FormulaRegistry : IEnumerable<FormulaDefinition>
{
    private readonly Dictionary<Guid, FormulaDefinition> _formulas = new();
    private readonly Dictionary<string, Guid> _targetKeyIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    
    // === Events ===
    
    /// <summary>Formül eklendiğinde veya güncellendiğinde.</summary>
    public event Action<FormulaDefinition>? FormulaChanged;
    
    /// <summary>Formül silindiğinde.</summary>
    public event Action<FormulaDefinition>? FormulaRemoved;
    
    // === Properties ===
    
    /// <summary>Toplam formül sayısı.</summary>
    public int Count
    {
        get { lock (_lock) return _formulas.Count; }
    }
    
    /// <summary>Görünür (hidden olmayan) formül sayısı.</summary>
    public int VisibleCount
    {
        get { lock (_lock) return _formulas.Values.Count(f => !f.IsHidden); }
    }
    
    // === Core Operations ===
    
    /// <summary>Formül ekle veya güncelle.</summary>
    public void Set(FormulaDefinition formula)
    {
        ArgumentNullException.ThrowIfNull(formula);
        var normalizedTarget = KeyNormalizer.Normalize(formula.TargetKey);
        var normalizedFormula = formula with { TargetKey = normalizedTarget };
        
        lock (_lock)
        {
            // Mevcut formülü güncelle
            if (_formulas.TryGetValue(formula.Id, out var existing))
            {
                // Eski target key index'ini temizle
                if (_targetKeyIndex.TryGetValue(existing.TargetKey, out var oldId) && oldId == formula.Id)
                    _targetKeyIndex.Remove(existing.TargetKey);
            }
            
            _formulas[formula.Id] = normalizedFormula;
            _targetKeyIndex[normalizedTarget] = formula.Id;
            FormulaChanged?.Invoke(normalizedFormula);
        }
    }
    
    /// <summary>Birden fazla formül ekle (batch).</summary>
    public void SetRange(IEnumerable<FormulaDefinition> formulas)
    {
        foreach (var formula in formulas)
            Set(formula);
    }
    
    /// <summary>ID ile formül getir.</summary>
    public FormulaDefinition? GetById(Guid id)
    {
        lock (_lock)
        {
            return _formulas.TryGetValue(id, out var formula) ? formula : null;
        }
    }
    
    /// <summary>Target key ile formül getir.</summary>
    public FormulaDefinition? GetByTargetKey(string targetKey)
    {
        var normalizedKey = KeyNormalizer.Normalize(targetKey);
        lock (_lock)
        {
            if (_targetKeyIndex.TryGetValue(normalizedKey, out var id))
                return _formulas.TryGetValue(id, out var formula) ? formula : null;
            return null;
        }
    }
    
    /// <summary>Formül mevcut mu?</summary>
    public bool Contains(Guid id)
    {
        lock (_lock) return _formulas.ContainsKey(id);
    }
    
    /// <summary>Target key için formül var mı?</summary>
    public bool HasFormulaFor(string targetKey)
    {
        var normalizedKey = KeyNormalizer.Normalize(targetKey);
        lock (_lock) return _targetKeyIndex.ContainsKey(normalizedKey);
    }
    
    // === Remove Operations ===
    
    /// <summary>Formülü ID ile sil.</summary>
    public bool Remove(Guid id)
    {
        lock (_lock)
        {
            if (!_formulas.TryGetValue(id, out var formula))
                return false;
            
            _formulas.Remove(id);
            if (_targetKeyIndex.TryGetValue(formula.TargetKey, out var indexedId) && indexedId == id)
                _targetKeyIndex.Remove(formula.TargetKey);
            
            FormulaRemoved?.Invoke(formula);
            return true;
        }
    }
    
    /// <summary>Formülü gizle (soft delete).</summary>
    public bool Hide(Guid id)
    {
        lock (_lock)
        {
            if (!_formulas.TryGetValue(id, out var formula))
                return false;
            
            var hidden = formula with { IsHidden = true };
            _formulas[id] = hidden;
            FormulaChanged?.Invoke(hidden);
            return true;
        }
    }
    
    // === Dependency Analysis ===
    
    /// <summary>Belirli bir key'i kullanan formülleri getir.</summary>
    public IReadOnlyList<FormulaDefinition> GetFormulasUsingKey(string key)
    {
        var normalizedKey = KeyNormalizer.Normalize(key);
        lock (_lock)
        {
            return _formulas.Values
                .Where(f => f.Dependencies.Contains(normalizedKey))
                .ToList();
        }
    }
    
    /// <summary>Hesaplama sırası için topooljik sıralama yap.</summary>
    public IReadOnlyList<FormulaDefinition> GetSortedForEvaluation()
    {
        lock (_lock)
        {
            // Basit topooljik sıralama (bağımlılıklara göre)
            var sorted = new List<FormulaDefinition>();
            var visited = new HashSet<Guid>();
            var formulas = _formulas.Values.Where(f => !f.IsHidden).ToList();
            
            void Visit(FormulaDefinition formula)
            {
                if (!visited.Add(formula.Id)) return;
                
                // Önce bağımlılıkları ziyaret et
                foreach (var dep in formula.Dependencies)
                {
                    if (_targetKeyIndex.TryGetValue(dep, out var depId) && 
                        _formulas.TryGetValue(depId, out var depFormula) &&
                        !depFormula.IsHidden)
                    {
                        Visit(depFormula);
                    }
                }
                
                sorted.Add(formula);
            }
            
            // SortOrder'a göre sırala, sonra topooljik sıralama uygula
            foreach (var formula in formulas.OrderBy(f => f.SortOrder))
            {
                Visit(formula);
            }
            
            return sorted;
        }
    }
    
    // === Bulk Operations ===
    
    /// <summary>Tüm formülleri temizle.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _formulas.Clear();
            _targetKeyIndex.Clear();
        }
    }
    
    /// <summary>Görünür formülleri liste olarak getir.</summary>
    public IReadOnlyList<FormulaDefinition> GetVisible()
    {
        lock (_lock) return _formulas.Values.Where(f => !f.IsHidden).OrderBy(f => f.SortOrder).ToList();
    }
    
    // === IEnumerable ===
    
    public IEnumerator<FormulaDefinition> GetEnumerator()
    {
        lock (_lock) return _formulas.Values.ToList().GetEnumerator();
    }
    
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
