using KasaManager.Application.Processing.Abstractions.Repositories;

namespace KasaManager.Infrastructure.Processing.InMemory;

/// <summary>
/// Basit thread-safe InMemory repository.
/// Not: Bu sınıf türetilebilir (sealed değil) çünkü her dataset için
/// tip bazlı repository interface'leri (IAksamKasaNesnesiRepository vb.) bu sınıftan türetiliyor.
/// </summary>
public class InMemoryReportRepository<T> : IReportRepository<T> where T : class
{
    private readonly object _lock = new();
    private List<T> _items = new();

    public void ReplaceAll(IReadOnlyList<T> items)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));
        lock (_lock)
        {
            _items = new List<T>(items);
        }
    }

    public IReadOnlyList<T> GetAll()
    {
        lock (_lock)
        {
            // Snapshot - dışarıya mutable liste vermiyoruz
            return _items.ToArray();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }
    }
}
