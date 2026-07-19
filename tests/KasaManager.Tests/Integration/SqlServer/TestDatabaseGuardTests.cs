namespace KasaManager.Tests.Integration.SqlServer;

public sealed class TestDatabaseGuardTests
{
    private const string SafeLocalDb = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Database=IgnoredBase";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingConnectionIsRejected(string? connection) =>
        Assert.Throws<InvalidOperationException>(() => TestDatabaseGuard.ParseBaseConnectionString(connection));

    [Fact]
    public void UnparseableConnectionIsRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            TestDatabaseGuard.ParseBaseConnectionString("Password='unterminated"));

    [Theory]
    [InlineData(@"Server=(localdb)\MSSQLLocalDB;Database=KasaManager")]
    [InlineData(@"Server=(localdb)\MSSQLLocalDB;Database=master")]
    [InlineData(@"Server=(localdb)\MSSQLLocalDB;Database=tempdb")]
    public void ProductionAndSystemDatabasesAreRejected(string connection) =>
        Assert.Throws<InvalidOperationException>(() => TestDatabaseGuard.ParseBaseConnectionString(connection));

    [Theory]
    [InlineData("BayAsLaptop")]
    [InlineData(".")]
    [InlineData("localhost")]
    [InlineData("(local)")]
    [InlineData("127.0.0.1")]
    public void ProductionAndDefaultServerAliasesAreRejected(string server) =>
        Assert.Throws<InvalidOperationException>(() =>
            TestDatabaseGuard.ParseBaseConnectionString($"Server={server};Integrated Security=true"));

    [Fact]
    public void AttachDbFilenameIsRejected() =>
        Assert.Throws<InvalidOperationException>(() => TestDatabaseGuard.ParseBaseConnectionString(
            @"Server=(localdb)\MSSQLLocalDB;AttachDbFilename=C:\temp\unsafe.mdf;Integrated Security=true"));

    [Fact]
    public void UserInstanceIsRejected() =>
        Assert.Throws<InvalidOperationException>(() => TestDatabaseGuard.ParseBaseConnectionString(
            @"Server=(localdb)\MSSQLLocalDB;User Instance=true;Integrated Security=true"));

    [Theory]
    [InlineData("KasaManager")]
    [InlineData("master")]
    [InlineData("Other_Test_0123456789abcdef0123456789abcdef")]
    [InlineData("KasaManager_C4_Test_0123456789abcdef0123456789abcde")]
    [InlineData("KasaManager_C4_Test_0123456789abcdef0123456789abcdef;")]
    [InlineData("KasaManager_C4_Test_0123456789abcdef0123456789abcde ")]
    public void InvalidGeneratedAndCleanupTargetsAreRejected(string databaseName) =>
        Assert.Throws<InvalidOperationException>(() => TestDatabaseGuard.ValidateGeneratedTarget(databaseName));

    [Fact]
    public void SafeLocalDbTargetIsAccepted()
    {
        var validated = TestDatabaseGuard.ParseBaseConnectionString(SafeLocalDb);
        Assert.Equal(SqlServerTestTargetKind.LocalDb, validated.TargetKind);
    }

    [Fact]
    public void ExplicitRemoteNamedInstanceIsAccepted()
    {
        var validated = TestDatabaseGuard.ParseBaseConnectionString(
            @"Server=dev-sql\KasaTests;Integrated Security=true");
        Assert.Equal(SqlServerTestTargetKind.NamedInstance, validated.TargetKind);
    }

    [Fact]
    public void GeneratedDatabaseNameIsUniqueAndMatchesDeterministicFormat()
    {
        var first = TestDatabaseGuard.CreateUniqueDatabaseName();
        var second = TestDatabaseGuard.CreateUniqueDatabaseName();

        TestDatabaseGuard.ValidateGeneratedTarget(first);
        TestDatabaseGuard.ValidateGeneratedTarget(second);
        Assert.NotEqual(first, second);
        Assert.StartsWith(TestDatabaseGuard.DatabasePrefix, first, StringComparison.Ordinal);
        Assert.Equal(TestDatabaseGuard.DatabasePrefix.Length + 32, first.Length);
    }

    [Fact]
    public void MasterAndTestConnectionsIgnoreTheBaseCatalog()
    {
        var validated = TestDatabaseGuard.ParseBaseConnectionString(SafeLocalDb);
        var databaseName = TestDatabaseGuard.CreateUniqueDatabaseName();

        Assert.Equal("master", TestDatabaseGuard.BuildMasterConnectionString(validated).InitialCatalog);
        Assert.Equal(databaseName, TestDatabaseGuard.BuildTestDatabaseConnectionString(validated, databaseName).InitialCatalog);
    }

    [Fact]
    public void RedactedDiagnosticsAndExceptionsNeverContainSecrets()
    {
        const string secret = "C4-Super-Secret-Value";
        var validated = TestDatabaseGuard.ParseBaseConnectionString(
            $@"Server=(localdb)\MSSQLLocalDB;User ID=tester;Password={secret}");
        var diagnostic = TestDatabaseGuard.RedactForDiagnostics(validated, TestDatabaseGuard.CreateUniqueDatabaseName());

        Assert.DoesNotContain(secret, diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", diagnostic, StringComparison.OrdinalIgnoreCase);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            TestDatabaseGuard.ParseBaseConnectionString($"Password='{secret}"));
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedTargetIsRevalidatedBeforeBuildingConnection()
    {
        var validated = TestDatabaseGuard.ParseBaseConnectionString(SafeLocalDb);
        Assert.Throws<InvalidOperationException>(() =>
            TestDatabaseGuard.BuildTestDatabaseConnectionString(validated, "KasaManager"));
    }
}
