using KasaManager.Application.Services.DataFirst;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;

namespace KasaManager.Tests.Application;

public class FactNormalizationIntegrationTests
{
    private KasaManagerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
            
        return new KasaManagerDbContext(options);
    }

    [Fact]
    public async Task NormalizeAndSaveShadowFactsAsync_IdempotencyAndCanonicalCheck()
    {
        // 1. Setup
        var db = CreateDbContext();
        var sut = new FactNormalizationService(db, NullLogger<FactNormalizationService>.Instance);

        File.WriteAllText("BankaTahsilat_Fake.xlsx", "test"); // Dummy file for hashing
        
        var date = new DateOnly(2026, 5, 10);
        var table = new ImportedTable
        {
            SourceFileName = "BankaTahsilat_Fake.xlsx",
            Kind = ImportFileKind.BankaTahsilat,
            Columns = new List<string> { "islem_tarihi", "islem_tutari" },
            Rows = new List<Dictionary<string, string?>>
            {
                new Dictionary<string, string?> { { "islem_tarihi", "10.05.2026" }, { "islem_tutari", "5500.5" } }
            }
        };

        // 2. Act - First Import
        await sut.NormalizeAndSaveShadowFactsAsync(table, date, "BankaTahsilat_Fake.xlsx");

        // 3. Assert - Insertion Check
        var batchCount = await db.ImportBatches.CountAsync();
        var facts = await db.DailyFacts.ToListAsync();
        
        Assert.Equal(1, batchCount);
        Assert.Equal(2, facts.Count);
        Assert.Contains(facts, f => f.CanonicalKey == "bankatahsilat_islem_tarihi");
        Assert.Contains(facts, f => f.CanonicalKey == "bankatahsilat_islem_tutari");
    }
}
