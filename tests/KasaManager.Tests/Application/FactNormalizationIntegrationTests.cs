using KasaManager.Application.Services.DataFirst;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasaManager.Tests.Application;

/// <summary>
/// FactNormalizationService testleri.
/// InMemory: Birim testler (idempotency, hata raporlama).
/// LocalDB: Paralel concurrency testi — gerçek SQL Server relational path.
/// </summary>
public class FactNormalizationIntegrationTests : IDisposable
{
    private const string FakeFileName = "BankaTahsilat_Fake_Test.xlsx";

    public void Dispose()
    {
        if (File.Exists(FakeFileName))
            File.Delete(FakeFileName);
    }

    private static KasaManagerDbContext CreateDbContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
            .Options;
        return new KasaManagerDbContext(options);
    }

    private ImportedTable CreateFakeTable()
    {
        return new ImportedTable
        {
            SourceFileName = FakeFileName,
            Kind = ImportFileKind.BankaTahsilat,
            Columns = new List<string> { "islem_tarihi", "islem_tutari" },
            Rows = new List<Dictionary<string, string?>>
            {
                new() { { "islem_tarihi", "10.05.2026" }, { "islem_tutari", "5500.5" } }
            }
        };
    }

    // ═══════════════════════════════════════════════════════════
    // TEST 1: İlk insert — idempotency ve canonical key kontrolü
    // ═══════════════════════════════════════════════════════════
    [Fact]
    public async Task NormalizeAndSaveShadowFactsAsync_IdempotencyAndCanonicalCheck()
    {
        var db = CreateDbContext();
        var sut = new FactNormalizationService(db, NullLogger<FactNormalizationService>.Instance);

        File.WriteAllText(FakeFileName, "test");
        var date = new DateOnly(2026, 5, 10);
        var table = CreateFakeTable();

        var result = await sut.NormalizeAndSaveShadowFactsAsync(table, date, FakeFileName);

        Assert.True(result.Success);
        Assert.Equal(2, result.RowsInserted);

        var batchCount = await db.ImportBatches.CountAsync();
        var facts = await db.DailyFacts.ToListAsync();

        Assert.Equal(1, batchCount);
        Assert.Equal(2, facts.Count);
        Assert.Contains(facts, f => f.CanonicalKey == "bankatahsilat_islem_tarihi");
        Assert.Contains(facts, f => f.CanonicalKey == "bankatahsilat_islem_tutari");
    }

    // ═══════════════════════════════════════════════════════════
    // TEST 2: Aynı dosyayı tekrar yükle — idempotent skip
    // ═══════════════════════════════════════════════════════════
    [Fact]
    public async Task NormalizeAndSaveShadowFactsAsync_SameHash_ReturnsSkipped()
    {
        var db = CreateDbContext();
        var sut = new FactNormalizationService(db, NullLogger<FactNormalizationService>.Instance);

        File.WriteAllText(FakeFileName, "test_idem");
        var date = new DateOnly(2026, 5, 10);
        var table = CreateFakeTable();

        var result1 = await sut.NormalizeAndSaveShadowFactsAsync(table, date, FakeFileName);
        Assert.True(result1.Success);
        Assert.Equal(2, result1.RowsInserted);

        var result2 = await sut.NormalizeAndSaveShadowFactsAsync(table, date, FakeFileName);
        Assert.True(result2.Success);
        Assert.Equal(0, result2.RowsInserted);
        Assert.Contains("zaten import edilmiş", result2.Error);

        Assert.Equal(1, await db.ImportBatches.CountAsync());
    }

    // ═══════════════════════════════════════════════════════════
    // TEST 3: Dispose edilmiş DbContext → SaveChanges patlar → Fail dönmeli
    // ═══════════════════════════════════════════════════════════
    [Fact]
    public async Task NormalizeAndSaveShadowFactsAsync_DisposedDbContext_ReturnsFail()
    {
        var db = CreateDbContext();
        db.Dispose(); // DbContext kapalı — SaveChangesAsync ObjectDisposedException fırlatır

        var sut = new FactNormalizationService(db, NullLogger<FactNormalizationService>.Instance);

        File.WriteAllText(FakeFileName, "disposed_test");
        var date = new DateOnly(2026, 5, 10);
        var table = CreateFakeTable();

        var result = await sut.NormalizeAndSaveShadowFactsAsync(table, date, FakeFileName);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    // ═══════════════════════════════════════════════════════════
    // TEST 4: InMemory paralel — semaphore + idempotency doğrulaması
    // ═══════════════════════════════════════════════════════════
    [Fact]
    public async Task Parallel_SameFile_NoException_InMemory()
    {
        var sharedDbName = $"ParallelTest_{Guid.NewGuid()}";

        File.WriteAllText(FakeFileName, "parallel_test");
        var date = new DateOnly(2026, 5, 15);

        const int parallelCount = 10;
        var tasks = new Task<ShadowIngestionResult>[parallelCount];

        for (int i = 0; i < parallelCount; i++)
        {
            var table = CreateFakeTable();
            tasks[i] = Task.Run(async () =>
            {
                var threadDb = CreateDbContext(sharedDbName);
                var sut = new FactNormalizationService(
                    threadDb, NullLogger<FactNormalizationService>.Instance);

                return await sut.NormalizeAndSaveShadowFactsAsync(table, date, FakeFileName);
            });
        }

        var results = await Task.WhenAll(tasks);

        Assert.Contains(results, r => r.Success && r.RowsInserted > 0);
        Assert.DoesNotContain(results, r => !r.Success);

        var checkDb = CreateDbContext(sharedDbName);
        var batchCount = await checkDb.ImportBatches.CountAsync();
        Assert.Equal(1, batchCount);
    }

    // ═══════════════════════════════════════════════════════════
    // TEST 5: GERÇEK SQL Server (LocalDB) paralel concurrency testi.
    //   Asıl DbUpdateConcurrencyException bug'ının düzeldiğini KANITLAR.
    //   ExecutionStrategy + Transaction + ExecuteDeleteAsync yolu test edilir.
    //   10 paralel thread aynı dosya+tarih → exception yok + tek batch.
    // ═══════════════════════════════════════════════════════════
    [Fact]
    public async Task Parallel_SameFile_NoException_SqlServer()
    {
        // LocalDB bağlantı — izole test DB'si
        var testDbName = $"FactNormTest_{Guid.NewGuid():N}";
        var connStr = $"Server=(localdb)\\mssqllocaldb;Database={testDbName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

        // DB'yi oluştur ve schema'yı kur
        var seedOptions = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseSqlServer(connStr, sql => sql.EnableRetryOnFailure(3))
            .Options;

        bool localDbAvailable;
        await using (var seedDb = new KasaManagerDbContext(seedOptions))
        {
            try
            {
                await seedDb.Database.EnsureCreatedAsync();
                localDbAvailable = true;
            }
            catch (SqlException)
            {
                // LocalDB kurulu değilse testi atla (CI ortamında)
                localDbAvailable = false;
            }
        }

        if (!localDbAvailable)
        {
            // Skip: LocalDB yoksa bu test çalışmaz — CI'da Testcontainers kullanılabilir.
            return;
        }

        try
        {
            File.WriteAllText(FakeFileName, "sqlserver_parallel_test");
            var date = new DateOnly(2026, 5, 20);

            const int parallelCount = 10;
            var tasks = new Task<ShadowIngestionResult>[parallelCount];

            for (int i = 0; i < parallelCount; i++)
            {
                var table = CreateFakeTable();
                tasks[i] = Task.Run(async () =>
                {
                    // Her thread kendi DbContext'ini alır (DI Scoped simülasyonu)
                    var threadOptions = new DbContextOptionsBuilder<KasaManagerDbContext>()
                        .UseSqlServer(connStr, sql => sql.EnableRetryOnFailure(3))
                        .Options;
                    var threadDb = new KasaManagerDbContext(threadOptions);

                    var sut = new FactNormalizationService(
                        threadDb, NullLogger<FactNormalizationService>.Instance);

                    return await sut.NormalizeAndSaveShadowFactsAsync(table, date, FakeFileName);
                });
            }

            // Hiçbiri exception fırlatmamalı (özellikle DbUpdateConcurrencyException!)
            var results = await Task.WhenAll(tasks);

            // En az 1'i başarıyla insert etmeli
            Assert.Contains(results, r => r.Success && r.RowsInserted > 0);

            // Hiçbiri Fail dönmemeli
            Assert.DoesNotContain(results, r => !r.Success);

            // DB'de tek batch kalmalı
            await using var checkDb = new KasaManagerDbContext(seedOptions);
            var batchCount = await checkDb.ImportBatches.CountAsync();
            Assert.Equal(1, batchCount);
        }
        finally
        {
            // Test DB'sini temizle
            await using var cleanDb = new KasaManagerDbContext(seedOptions);
            await cleanDb.Database.EnsureDeletedAsync();
        }
    }
}
