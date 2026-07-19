using KasaManager.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Tests.Integration.SqlServer;

public sealed class SqlServerIntegrationFixture : IAsyncLifetime
{
    private ValidatedBaseConnection? _validatedBase;
    private DbContextOptions<KasaManagerDbContext>? _options;
    private bool _databaseCreated;

    internal string DatabaseName { get; private set; } = string.Empty;
    internal string DiagnosticTarget { get; private set; } = string.Empty;
    internal IReadOnlyList<string> Migrations { get; private set; } = [];
    internal IReadOnlyList<string> AppliedMigrations { get; private set; } = [];
    internal bool CleanupVerified { get; private set; }

    public async Task InitializeAsync()
    {
        var rawConnection = Environment.GetEnvironmentVariable(TestDatabaseGuard.EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(rawConnection))
            return;

        _validatedBase = TestDatabaseGuard.ParseBaseConnectionString(rawConnection);
        DatabaseName = TestDatabaseGuard.CreateUniqueDatabaseName();
        TestDatabaseGuard.ValidateGeneratedTarget(DatabaseName);
        DiagnosticTarget = TestDatabaseGuard.RedactForDiagnostics(_validatedBase, DatabaseName);

        try
        {
            await CreateDatabaseAsync(_validatedBase, DatabaseName);
            _databaseCreated = true;

            var testConnection = TestDatabaseGuard.BuildTestDatabaseConnectionString(_validatedBase, DatabaseName);
            var migrationAssembly = typeof(KasaManagerDbContext).Assembly.GetName().Name;
            _options = new DbContextOptionsBuilder<KasaManagerDbContext>()
                .UseSqlServer(
                    testConnection.ConnectionString,
                    sql => sql.MigrationsAssembly(migrationAssembly).EnableRetryOnFailure(3))
                .Options;

            await using var context = CreateContext();
            Migrations = context.Database.GetMigrations().ToArray();
            await context.Database.MigrateAsync();
            AppliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        }
        catch
        {
            if (_databaseCreated)
            {
                try
                {
                    await DropDatabaseAsync(_validatedBase, DatabaseName);
                }
                catch
                {
                    // Preserve the initialization exception; cleanup is verified again by the acceptance run.
                }
            }

            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (!_databaseCreated || _validatedBase is null)
            return;

        TestDatabaseGuard.ValidateGeneratedTarget(DatabaseName);
        await DropDatabaseAsync(_validatedBase, DatabaseName);
        CleanupVerified = !await DatabaseExistsAsync(_validatedBase, DatabaseName);
        if (!CleanupVerified)
            throw new InvalidOperationException("The disposable C4 SQL Server database could not be removed.");
    }

    internal KasaManagerDbContext CreateContext()
    {
        if (_options is null)
            throw new InvalidOperationException("The SQL Server integration fixture is not enabled.");
        return new KasaManagerDbContext(_options);
    }

    internal ValidatedBaseConnection GetValidatedBase() =>
        _validatedBase ?? throw new InvalidOperationException("The SQL Server integration fixture is not enabled.");

    internal static async Task CreateDatabaseAsync(ValidatedBaseConnection validated, string databaseName)
    {
        TestDatabaseGuard.ValidateGeneratedTarget(databaseName);
        var masterBuilder = TestDatabaseGuard.BuildMasterConnectionString(validated);
        await using var connection = new SqlConnection(masterBuilder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
    }

    internal static async Task DropDatabaseAsync(ValidatedBaseConnection validated, string databaseName)
    {
        TestDatabaseGuard.ValidateGeneratedTarget(databaseName);
        var masterBuilder = TestDatabaseGuard.BuildMasterConnectionString(validated);
        await using var connection = new SqlConnection(masterBuilder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(@databaseName) IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END
            """;
        command.Parameters.AddWithValue("@databaseName", databaseName);
        await command.ExecuteNonQueryAsync();
    }

    internal static async Task<bool> DatabaseExistsAsync(ValidatedBaseConnection validated, string databaseName)
    {
        TestDatabaseGuard.ValidateGeneratedTarget(databaseName);
        var masterBuilder = TestDatabaseGuard.BuildMasterConnectionString(validated);
        await using var connection = new SqlConnection(masterBuilder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN DB_ID(@databaseName) IS NULL THEN 0 ELSE 1 END";
        command.Parameters.AddWithValue("@databaseName", databaseName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }
}
