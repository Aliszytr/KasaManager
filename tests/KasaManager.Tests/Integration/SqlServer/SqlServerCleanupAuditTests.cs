using Microsoft.Data.SqlClient;

namespace KasaManager.Tests.Integration.SqlServer;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class SqlServerCleanupAuditCollection
{
    public const string CollectionName = "SqlServerCleanupAudit";
}

[Collection(SqlServerCleanupAuditCollection.CollectionName)]
public sealed class SqlServerCleanupAuditTests
{
    [SqlServerFact]
    public async Task NoDisposableC4DatabaseRemainsOnTheGuardedServer()
    {
        var rawConnection = Environment.GetEnvironmentVariable(TestDatabaseGuard.EnvironmentVariableName);
        var validated = TestDatabaseGuard.ParseBaseConnectionString(rawConnection);
        var master = TestDatabaseGuard.BuildMasterConnectionString(validated);

        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [name]
            FROM sys.databases
            WHERE [name] LIKE 'KasaManager[_]C4[_]Test[_]%'
            ORDER BY [name]
            """;

        var remaining = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            remaining.Add(reader.GetString(0));

        Assert.Empty(remaining);
    }
}
