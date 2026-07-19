namespace KasaManager.Tests.Integration.SqlServer;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerIntegrationCollection : ICollectionFixture<SqlServerIntegrationFixture>
{
    public const string Name = "SqlServerIntegration";
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TestDatabaseGuard.EnvironmentVariableName)))
        {
            Skip = $"Set {TestDatabaseGuard.EnvironmentVariableName} to an explicitly safe SQL Server test connection.";
        }
    }
}
