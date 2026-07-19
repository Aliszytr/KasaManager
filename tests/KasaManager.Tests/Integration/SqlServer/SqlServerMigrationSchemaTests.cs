using System.Data.Common;
using KasaManager.Domain.Calculation.Data;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace KasaManager.Tests.Integration.SqlServer;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class SqlServerMigrationSchemaTests(SqlServerIntegrationFixture fixture)
{
    private const string U2Migration = "20260718144052_AddHesapKontrolActorAudit";
    private const string U3Migration = "20260718152130_AddCalculatedKasaSnapshotActorAudit";

    [SqlServerFact]
    public async Task DisposableDatabaseIsCreatedOnTheGuardedTarget()
    {
        TestDatabaseGuard.ValidateGeneratedTarget(fixture.DatabaseName);
        Assert.StartsWith(TestDatabaseGuard.DatabasePrefix, fixture.DatabaseName, StringComparison.Ordinal);
        Assert.StartsWith("Target=", fixture.DiagnosticTarget, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", fixture.DiagnosticTarget, StringComparison.OrdinalIgnoreCase);
        Assert.True(await SqlServerIntegrationFixture.DatabaseExistsAsync(
            fixture.GetValidatedBase(), fixture.DatabaseName));
    }

    [SqlServerFact]
    public async Task EmptyDatabaseReceivesTheCompleteMigrationChainAndHistory()
    {
        await using var context = fixture.CreateContext();
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToArray();
        var history = await QueryStringsAsync(context, "SELECT [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId]");

        Assert.NotEmpty(fixture.Migrations);
        Assert.Empty(pending);
        Assert.Equal(fixture.Migrations.Order(StringComparer.Ordinal), fixture.AppliedMigrations.Order(StringComparer.Ordinal));
        Assert.Equal(fixture.AppliedMigrations.Order(StringComparer.Ordinal), history.Order(StringComparer.Ordinal));
        Assert.Contains(U2Migration, history);
        Assert.Contains(U3Migration, history);
        Assert.True(Array.IndexOf(history, U2Migration) < Array.IndexOf(history, U3Migration));
        Assert.DoesNotContain(fixture.Migrations, id => id.Contains("C3", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(typeof(KasaManagerDbContext).Assembly.GetName().Name, context.GetType().Assembly.GetName().Name);
    }

    [SqlServerFact]
    public async Task HesapKontrolActorColumnsMatchTheModelWithoutConstraintsOrIndexes()
    {
        await using var context = fixture.CreateContext();
        var table = GetTable<HesapKontrolKaydi>(context);
        string[] properties =
        [
            nameof(HesapKontrolKaydi.CreatedByUserId),
            nameof(HesapKontrolKaydi.TrackingStartedByUserId),
            nameof(HesapKontrolKaydi.ResolvedByUserId),
            nameof(HesapKontrolKaydi.ApprovedByUserId),
            nameof(HesapKontrolKaydi.CancelledByUserId)
        ];

        foreach (var property in properties)
        {
            var column = GetColumnName<HesapKontrolKaydi>(context, property);
            var schema = await GetColumnSchemaAsync(context, table, column);
            Assert.Equal("int", schema.SqlType);
            Assert.True(schema.IsNullable);
            Assert.False(schema.HasDefault);
            Assert.False(schema.HasForeignKey);
            Assert.False(schema.HasIndex);
        }

        await AssertPhysicalColumnsMatchEfModelAsync<HesapKontrolKaydi>(context);
    }

    [SqlServerFact]
    public async Task SnapshotActorJsonAndSoftDeleteColumnsMatchTheModel()
    {
        await using var context = fixture.CreateContext();
        var table = GetTable<CalculatedKasaSnapshot>(context);

        foreach (var property in new[]
                 {
                     nameof(CalculatedKasaSnapshot.CalculatedByUserId),
                     nameof(CalculatedKasaSnapshot.DeletedByUserId)
                 })
        {
            var schema = await GetColumnSchemaAsync(
                context, table, GetColumnName<CalculatedKasaSnapshot>(context, property));
            Assert.Equal("int", schema.SqlType);
            Assert.True(schema.IsNullable);
            Assert.False(schema.HasDefault);
            Assert.False(schema.HasForeignKey);
            Assert.False(schema.HasIndex);
        }

        var json = await GetColumnSchemaAsync(
            context, table, GetColumnName<CalculatedKasaSnapshot>(context, nameof(CalculatedKasaSnapshot.KasaRaporDataJson)));
        Assert.Equal("nvarchar", json.SqlType);
        Assert.Equal(-1, json.MaxLength);

        await AssertColumnAsync<CalculatedKasaSnapshot>(context, nameof(CalculatedKasaSnapshot.IsDeleted), "bit", false);
        await AssertColumnAsync<CalculatedKasaSnapshot>(context, nameof(CalculatedKasaSnapshot.DeletedAtUtc), "datetime2", true);
        await AssertColumnAsync<CalculatedKasaSnapshot>(context, nameof(CalculatedKasaSnapshot.DeletedBy), "nvarchar", true);
        await AssertColumnAsync<CalculatedKasaSnapshot>(context, nameof(CalculatedKasaSnapshot.Version), "int", false);
        await AssertPhysicalColumnsMatchEfModelAsync<CalculatedKasaSnapshot>(context);
    }

    [SqlServerFact]
    public async Task CriticalDecimalDateOnlyFingerprintAndUniqueIndexSchemaIsUsable()
    {
        await using var context = fixture.CreateContext();

        var amount = await GetColumnSchemaAsync(
            context,
            GetTable<HesapKontrolKaydi>(context),
            GetColumnName<HesapKontrolKaydi>(context, nameof(HesapKontrolKaydi.Tutar)));
        Assert.Equal("decimal", amount.SqlType);
        Assert.Equal(18, amount.Precision);
        Assert.Equal(2, amount.Scale);

        await AssertColumnAsync<HesapKontrolKaydi>(context, nameof(HesapKontrolKaydi.AnalizTarihi), "datetime2", false);
        await AssertColumnAsync<DailyCalculationResult>(context, nameof(DailyCalculationResult.ForDate), "datetime2", false);
        Assert.Equal(0, await context.HesapKontrolKayitlari.CountAsync(x => x.AnalizTarihi == new DateOnly(2040, 1, 2)));

        var fingerprint = await GetColumnSchemaAsync(
            context,
            GetTable<DailyCalculationResult>(context),
            GetColumnName<DailyCalculationResult>(context, nameof(DailyCalculationResult.InputsFingerprint)));
        Assert.Equal("nvarchar", fingerprint.SqlType);
        Assert.Equal(510, fingerprint.MaxLength);
        await AssertColumnAsync<DailyCalculationResult>(context, nameof(DailyCalculationResult.CalculatedVersion), "int", false);

        var entity = context.Model.FindEntityType(typeof(DailyCalculationResult))!;
        var index = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(
                [nameof(DailyCalculationResult.ForDate), nameof(DailyCalculationResult.KasaTuru)]));
        var indexName = index.GetDatabaseName()!;
        var indexColumns = await QueryStringsAsync(
            context,
            """
            SELECT c.name
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE t.name = @table AND i.name = @index AND i.is_unique = 1
            ORDER BY ic.key_ordinal
            """,
            ("@table", GetTable<DailyCalculationResult>(context)),
            ("@index", indexName));
        Assert.Equal(
            [
                GetColumnName<DailyCalculationResult>(context, nameof(DailyCalculationResult.ForDate)),
                GetColumnName<DailyCalculationResult>(context, nameof(DailyCalculationResult.KasaTuru))
            ],
            indexColumns);
    }

    [SqlServerFact]
    public async Task PrefixGuardedLifecycleCreatesAndDropsOnlyItsDisposableDatabase()
    {
        var validated = fixture.GetValidatedBase();
        var databaseName = TestDatabaseGuard.CreateUniqueDatabaseName();
        await SqlServerIntegrationFixture.CreateDatabaseAsync(validated, databaseName);

        try
        {
            Assert.True(await SqlServerIntegrationFixture.DatabaseExistsAsync(validated, databaseName));
        }
        finally
        {
            await SqlServerIntegrationFixture.DropDatabaseAsync(validated, databaseName);
        }

        Assert.False(await SqlServerIntegrationFixture.DatabaseExistsAsync(validated, databaseName));
    }

    private static async Task AssertColumnAsync<TEntity>(
        KasaManagerDbContext context,
        string propertyName,
        string sqlType,
        bool nullable)
    {
        var schema = await GetColumnSchemaAsync(
            context, GetTable<TEntity>(context), GetColumnName<TEntity>(context, propertyName));
        Assert.Equal(sqlType, schema.SqlType);
        Assert.Equal(nullable, schema.IsNullable);
    }

    private static string GetTable<TEntity>(KasaManagerDbContext context) =>
        context.Model.FindEntityType(typeof(TEntity))?.GetTableName()
        ?? throw new InvalidOperationException($"No SQL table mapping exists for {typeof(TEntity).Name}.");

    private static string GetColumnName<TEntity>(KasaManagerDbContext context, string propertyName)
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))
                     ?? throw new InvalidOperationException($"No EF mapping exists for {typeof(TEntity).Name}.");
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        return entity.FindProperty(propertyName)?.GetColumnName(table)
               ?? throw new InvalidOperationException($"No SQL column mapping exists for {typeof(TEntity).Name}.{propertyName}.");
    }

    private static async Task AssertPhysicalColumnsMatchEfModelAsync<TEntity>(KasaManagerDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        var tableName = entity.GetTableName()!;
        var storeObject = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        var expected = entity.GetProperties()
            .Select(property => property.GetColumnName(storeObject))
            .Where(name => name is not null)
            .Cast<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var actual = (await QueryStringsAsync(
                context,
                "SELECT c.name FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.name = @table",
                ("@table", tableName)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.True(
            expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase),
            $"Physical columns for {tableName} differ from the EF model.");
    }

    private static async Task<ColumnSchema> GetColumnSchemaAsync(
        KasaManagerDbContext context,
        string tableName,
        string columnName)
    {
        const string sql = """
            SELECT ty.name, c.max_length, c.precision, c.scale, c.is_nullable,
                   CASE WHEN dc.object_id IS NULL THEN 0 ELSE 1 END,
                   CASE WHEN EXISTS (
                       SELECT 1 FROM sys.foreign_key_columns fkc
                       WHERE fkc.parent_object_id = c.object_id AND fkc.parent_column_id = c.column_id
                   ) THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS (
                       SELECT 1 FROM sys.index_columns ic
                       WHERE ic.object_id = c.object_id AND ic.column_id = c.column_id
                   ) THEN 1 ELSE 0 END
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
            WHERE t.name = @table AND c.name = @column
            """;

        var connection = context.Database.GetDbConnection();
        await EnsureOpenAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@table", tableName);
        AddParameter(command, "@column", columnName);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"Column {tableName}.{columnName} was not found.");
        return new ColumnSchema(
            reader.GetString(0),
            reader.GetInt16(1),
            reader.GetByte(2),
            reader.GetByte(3),
            reader.GetBoolean(4),
            reader.GetInt32(5) == 1,
            reader.GetInt32(6) == 1,
            reader.GetInt32(7) == 1);
    }

    private static async Task<string[]> QueryStringsAsync(
        KasaManagerDbContext context,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        var connection = context.Database.GetDbConnection();
        await EnsureOpenAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
            AddParameter(command, parameter.Name, parameter.Value);

        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return values.ToArray();
    }

    private static async Task EnsureOpenAsync(DbConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record ColumnSchema(
        string SqlType,
        short MaxLength,
        byte Precision,
        byte Scale,
        bool IsNullable,
        bool HasDefault,
        bool HasForeignKey,
        bool HasIndex);
}
