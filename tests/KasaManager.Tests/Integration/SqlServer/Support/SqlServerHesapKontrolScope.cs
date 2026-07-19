using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Tests.Integration.SqlServer.Support;

internal sealed class SqlServerHesapKontrolScope : IAsyncDisposable
{
    private readonly SqlServerIntegrationFixture _fixture;
    private readonly HashSet<Guid> _recordIds = [];

    public SqlServerHesapKontrolScope(SqlServerIntegrationFixture fixture)
    {
        _fixture = fixture;
        Context = fixture.CreateContext();
    }

    public KasaManagerDbContext Context { get; }

    public void AddRange(params HesapKontrolKaydi[] records)
    {
        foreach (var record in records)
            _recordIds.Add(record.Id);

        Context.AddRange(records);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        if (_recordIds.Count == 0)
            return;

        await using var cleanup = _fixture.CreateContext();
        await cleanup.HesapKontrolKayitlari
            .Where(record => _recordIds.Contains(record.Id))
            .ExecuteDeleteAsync();
    }
}
