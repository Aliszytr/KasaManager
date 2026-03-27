#nullable enable
using KasaManager.Application.Processing.Abstractions.Repositories;
using KasaManager.Domain.Models;
using KasaManager.Domain.Reports;

namespace KasaManager.Infrastructure.Processing.InMemory;

/// <summary>
/// REFACTOR R1: InMemory implementation of IUnifiedKasaRepository.
/// 
/// Thread-safe, dictionary-based storage for UnifiedKasaRecord.
/// Key: (RaporTarihi, RaporTuru) tuple.
/// </summary>
public sealed class InMemoryUnifiedKasaRepository : IUnifiedKasaRepository
{
    private readonly object _lock = new();
    private readonly Dictionary<(DateOnly, KasaRaporTuru), UnifiedKasaRecord> _records = new();

    public void Upsert(UnifiedKasaRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        
        lock (_lock)
        {
            var key = (record.RaporTarihi, record.RaporTuru);
            _records[key] = record;
        }
    }

    public UnifiedKasaRecord? Get(DateOnly raporTarihi, KasaRaporTuru turu)
    {
        lock (_lock)
        {
            return _records.TryGetValue((raporTarihi, turu), out var record) ? record : null;
        }
    }

    public IReadOnlyList<UnifiedKasaRecord> GetByDateRange(DateOnly startDate, DateOnly endDate, KasaRaporTuru? turu = null)
    {
        lock (_lock)
        {
            return _records.Values
                .Where(r => r.RaporTarihi >= startDate && r.RaporTarihi <= endDate)
                .Where(r => turu == null || r.RaporTuru == turu)
                .OrderBy(r => r.RaporTarihi)
                .ThenBy(r => r.RaporTuru)
                .ToList();
        }
    }

    public IReadOnlyList<UnifiedKasaRecord> GetByType(KasaRaporTuru turu)
    {
        lock (_lock)
        {
            return _records.Values
                .Where(r => r.RaporTuru == turu)
                .OrderBy(r => r.RaporTarihi)
                .ToList();
        }
    }

    public IReadOnlyList<UnifiedKasaRecord> GetAll()
    {
        lock (_lock)
        {
            return _records.Values
                .OrderBy(r => r.RaporTarihi)
                .ThenBy(r => r.RaporTuru)
                .ToList();
        }
    }

    public bool Remove(DateOnly raporTarihi, KasaRaporTuru turu)
    {
        lock (_lock)
        {
            return _records.Remove((raporTarihi, turu));
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _records.Clear();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _records.Count;
            }
        }
    }
}
