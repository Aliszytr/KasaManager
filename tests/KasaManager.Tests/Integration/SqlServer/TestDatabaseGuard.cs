using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace KasaManager.Tests.Integration.SqlServer;

internal enum SqlServerTestTargetKind
{
    LocalDb,
    NamedInstance
}

internal sealed record ValidatedBaseConnection(
    SqlConnectionStringBuilder ConnectionString,
    SqlServerTestTargetKind TargetKind);

internal static partial class TestDatabaseGuard
{
    internal const string EnvironmentVariableName = "KASAMANAGER_C4_SQLSERVER";
    internal const string DatabasePrefix = "KasaManager_C4_Test_";

    private static readonly string[] SystemDatabases = ["master", "tempdb", "model", "msdb"];
    private static readonly char[] UnsafeDatabaseNameCharacters = ['[', ']', '\'', '"', ';'];

    internal static ValidatedBaseConnection ParseBaseConnectionString(string? rawConnectionString)
    {
        if (string.IsNullOrWhiteSpace(rawConnectionString))
            throw new InvalidOperationException("A SQL Server test connection was not supplied.");

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(rawConnectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("The SQL Server test connection is invalid.");
        }

        if (!string.IsNullOrWhiteSpace(builder.AttachDBFilename))
            throw new InvalidOperationException("AttachDbFilename is forbidden for SQL Server tests.");
        if (builder.UserInstance)
            throw new InvalidOperationException("User Instance is forbidden for SQL Server tests.");

        ValidateBaseDatabase(builder.InitialCatalog);
        var targetKind = ValidateServer(builder.DataSource);

        return new ValidatedBaseConnection(builder, targetKind);
    }

    internal static SqlServerTestTargetKind ValidateServer(string? dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource))
            throw new InvalidOperationException("The SQL Server test target is missing.");

        var server = dataSource.Trim();
        if (server.Equals(@"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase))
            return SqlServerTestTargetKind.LocalDb;

        var host = server.Split(['\\', ','], 2)[0].Trim();
        var localAliases = new[]
        {
            ".", "(local)", "localhost", "127.0.0.1", "::1", "BayAsLaptop", Environment.MachineName
        };

        if (localAliases.Any(alias => host.Equals(alias, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The production/default SQL Server target is forbidden.");

        if (server.IndexOfAny([';', '\'', '"', '[', ']']) >= 0 || server.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("The SQL Server test target contains unsafe characters.");

        var slash = server.IndexOf('\\');
        if (slash <= 0 || slash == server.Length - 1 || server.IndexOf('\\', slash + 1) >= 0)
            throw new InvalidOperationException("Only LocalDB or an explicit non-local named test instance is allowed.");

        return SqlServerTestTargetKind.NamedInstance;
    }

    internal static string CreateUniqueDatabaseName() => $"{DatabasePrefix}{Guid.NewGuid():N}";

    internal static SqlConnectionStringBuilder BuildMasterConnectionString(ValidatedBaseConnection validated)
    {
        var builder = Clone(validated.ConnectionString);
        builder.InitialCatalog = "master";
        builder.AttachDBFilename = string.Empty;
        builder.UserInstance = false;
        return builder;
    }

    internal static SqlConnectionStringBuilder BuildTestDatabaseConnectionString(
        ValidatedBaseConnection validated,
        string databaseName)
    {
        ValidateGeneratedTarget(databaseName);
        var builder = Clone(validated.ConnectionString);
        builder.InitialCatalog = databaseName;
        builder.AttachDBFilename = string.Empty;
        builder.UserInstance = false;
        return builder;
    }

    internal static void ValidateGeneratedTarget(string? databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName) || !GeneratedDatabaseNameRegex().IsMatch(databaseName))
            throw new InvalidOperationException("The disposable database name is outside the C4 test contract.");

        if (databaseName.Equals("KasaManager", StringComparison.OrdinalIgnoreCase) ||
            SystemDatabases.Contains(databaseName, StringComparer.OrdinalIgnoreCase) ||
            databaseName.IndexOfAny(UnsafeDatabaseNameCharacters) >= 0 ||
            databaseName.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException("The disposable database target is forbidden.");
        }
    }

    internal static string RedactForDiagnostics(ValidatedBaseConnection validated, string? databaseName = null)
    {
        if (databaseName is not null)
            ValidateGeneratedTarget(databaseName);

        var target = validated.TargetKind == SqlServerTestTargetKind.LocalDb ? "LocalDB" : "NamedTestInstance";
        return databaseName is null ? $"Target={target}" : $"Target={target};Database={databaseName}";
    }

    private static void ValidateBaseDatabase(string? initialCatalog)
    {
        if (string.IsNullOrWhiteSpace(initialCatalog))
            return;

        var database = initialCatalog.Trim();
        if (database.Equals("KasaManager", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The production database is forbidden.");
        if (SystemDatabases.Contains(database, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("A system database cannot be used as the test target.");
        if (database.IndexOfAny(UnsafeDatabaseNameCharacters) >= 0 || database.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("The base database name contains unsafe characters.");
    }

    private static SqlConnectionStringBuilder Clone(SqlConnectionStringBuilder source) =>
        new(source.ConnectionString);

    [GeneratedRegex("^KasaManager_C4_Test_[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedDatabaseNameRegex();
}
