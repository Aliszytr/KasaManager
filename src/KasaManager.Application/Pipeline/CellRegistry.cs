#nullable enable
using System.Collections;

namespace KasaManager.Application.Pipeline;

/// <summary>
/// R20 Wave 1: Cell Registry - merkezi veri deposu.
/// Tüm hücreleri tutar ve değişiklik olayları yayınlar.
/// Thread-safe ve reaktif.
/// </summary>
public sealed class CellRegistry : IEnumerable<Cell>
{
    private readonly Dictionary<string, Cell> _cells = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    
    // === Events (Reactive) ===
    
    /// <summary>Hücre eklendiğinde veya güncellendiğinde.</summary>
    public event Action<Cell>? CellChanged;
    
    /// <summary>Hücre seçimi değiştiğinde.</summary>
    public event Action<Cell, bool>? SelectionChanged;
    
    // === Properties ===
    
    /// <summary>Toplam hücre sayısı.</summary>
    public int Count
    {
        get { lock (_lock) return _cells.Count; }
    }
    
    /// <summary>Seçili hücre sayısı.</summary>
    public int SelectedCount
    {
        get { lock (_lock) return _cells.Values.Count(c => c.IsSelected); }
    }
    
    /// <summary>Tüm key'ler.</summary>
    public IReadOnlyCollection<string> Keys
    {
        get { lock (_lock) return _cells.Keys.ToList(); }
    }
    
    // === Core Operations ===
    
    /// <summary>Hücre ekle veya güncelle.</summary>
    public void Set(Cell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        var normalizedKey = KeyNormalizer.Normalize(cell.Key);
        
        lock (_lock)
        {
            // Mevcut hücreyi güncelle (seçim durumunu koru)
            if (_cells.TryGetValue(normalizedKey, out var existing))
            {
                var updated = cell with 
                { 
                    Key = normalizedKey,
                    IsSelected = existing.IsSelected 
                };
                // UsedByFormulas'ı kopyala
                foreach (var fId in existing.UsedByFormulas)
                    updated.UsedByFormulas.Add(fId);
                    
                _cells[normalizedKey] = updated;
                CellChanged?.Invoke(updated);
            }
            else
            {
                var newCell = cell with { Key = normalizedKey };
                _cells[normalizedKey] = newCell;
                CellChanged?.Invoke(newCell);
            }
        }
    }
    
    /// <summary>Birden fazla hücre ekle (batch).</summary>
    public void SetRange(IEnumerable<Cell> cells)
    {
        foreach (var cell in cells)
            Set(cell);
    }
    
    /// <summary>Key ile hücre getir.</summary>
    public Cell? Get(string key)
    {
        var normalizedKey = KeyNormalizer.Normalize(key);
        lock (_lock)
        {
            return _cells.TryGetValue(normalizedKey, out var cell) ? cell : null;
        }
    }
    
    /// <summary>Key ile hücre değeri getir.</summary>
    public decimal GetValue(string key, decimal defaultValue = 0m)
    {
        return Get(key)?.Value ?? defaultValue;
    }
    
    /// <summary>Key mevcut mu?</summary>
    public bool ContainsKey(string key)
    {
        var normalizedKey = KeyNormalizer.Normalize(key);
        lock (_lock) return _cells.ContainsKey(normalizedKey);
    }
    
    // === Selection Operations ===
    
    /// <summary>Hücreyi seç.</summary>
    public void Select(string key)
    {
        var normalizedKey = KeyNormalizer.Normalize(key);
        lock (_lock)
        {
            if (_cells.TryGetValue(normalizedKey, out var cell) && !cell.IsSelected)
            {
                cell.IsSelected = true;
                SelectionChanged?.Invoke(cell, true);
            }
        }
    }
    
    /// <summary>Hücre seçimini kaldır (formül kontrolü ile).</summary>
    public (bool success, string? warning) TryDeselect(string key)
    {
        var normalizedKey = KeyNormalizer.Normalize(key);
        lock (_lock)
        {
            if (!_cells.TryGetValue(normalizedKey, out var cell))
                return (true, null);
            
            if (!cell.IsSelected)
                return (true, null);
            
            // Formülde kullanılıyor mu?
            if (cell.UsedByFormulas.Count > 0)
            {
                return (false, $"'{key}' {cell.UsedByFormulas.Count} formülde kullanılıyor. " +
                              "Önce formüllerden kaldırın.");
            }
            
            cell.IsSelected = false;
            SelectionChanged?.Invoke(cell, false);
            return (true, null);
        }
    }
    
    /// <summary>Seçili hücreleri getir.</summary>
    public IReadOnlyList<Cell> GetSelected()
    {
        lock (_lock) return _cells.Values.Where(c => c.IsSelected).ToList();
    }
    
    /// <summary>Seçili key'leri getir.</summary>
    public IReadOnlyList<string> GetSelectedKeys()
    {
        lock (_lock) return _cells.Values.Where(c => c.IsSelected).Select(c => c.Key).ToList();
    }
    
    // === Formula Reference Tracking ===
    
    /// <summary>Hücreyi formüle bağla.</summary>
    public void AddFormulaReference(string key, Guid formulaId)
    {
        var normalizedKey = KeyNormalizer.Normalize(key);
        lock (_lock)
        {
            if (_cells.TryGetValue(normalizedKey, out var cell))
            {
                cell.UsedByFormulas.Add(formulaId);
            }
        }
    }
    
    /// <summary>Formül referansını kaldır.</summary>
    public void RemoveFormulaReference(string key, Guid formulaId)
    {
        var normalizedKey = KeyNormalizer.Normalize(key);
        lock (_lock)
        {
            if (_cells.TryGetValue(normalizedKey, out var cell))
            {
                cell.UsedByFormulas.Remove(formulaId);
            }
        }
    }
    
    // === Bulk Operations ===
    
    /// <summary>Tüm hücreleri temizle.</summary>
    public void Clear()
    {
        lock (_lock) _cells.Clear();
    }
    
    /// <summary>Dictionary olarak dışa aktar.</summary>
    public IReadOnlyDictionary<string, Cell> ToDictionary()
    {
        lock (_lock) return new Dictionary<string, Cell>(_cells, StringComparer.OrdinalIgnoreCase);
    }
    
    // === IEnumerable ===
    
    public IEnumerator<Cell> GetEnumerator()
    {
        lock (_lock) return _cells.Values.ToList().GetEnumerator();
    }
    
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
