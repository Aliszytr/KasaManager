using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Infrastructure.Services;
using Moq;

namespace KasaManager.Tests.Infrastructure;

/// <summary>
/// KasaManager Revision 3 Manual Resolve Write-BusinessDate Surgical Closure — targeted tests for
/// IManualResolveWriteBusinessDateResolver. Proves the fail-closed rule table: no analyzable Excel
/// date, conflicting Excel date, Excel date equal to or older than the last persisted Genel Kasa
/// date all fail closed with no business date returned; only a strictly newer Excel date (or no
/// persisted Kasa at all) succeeds. Never touches HesapKontrol records or the system clock.
/// </summary>
public sealed class ManualResolveWriteBusinessDateResolverTests
{
    private const string UploadFolder = @"C:\FakeUpload";

    [Fact]
    public async Task NoPersistedKasa_ValidAnalyzableExcel_SucceedsWithExcelDate()
    {
        var excelDate = new DateOnly(2026, 8, 20);
        var dateRules = MockDateRules(excelDate, hasConflict: false, hasAny: true);
        var snapshots = MockSnapshots(null);

        var resolver = new ManualResolveWriteBusinessDateResolver(dateRules.Object, snapshots.Object);
        var result = await resolver.ResolveAsync(UploadFolder);

        Assert.True(result.Success);
        Assert.Equal(excelDate, result.BusinessDate);
    }

    [Fact]
    public async Task ExcelDateAfterPersistedKasa_Succeeds()
    {
        var persisted = new DateOnly(2026, 8, 18);
        var excelDate = new DateOnly(2026, 8, 20);
        var dateRules = MockDateRules(excelDate, hasConflict: false, hasAny: true);
        var snapshots = MockSnapshots(new KasaRaporSnapshot { RaporTarihi = persisted });

        var resolver = new ManualResolveWriteBusinessDateResolver(dateRules.Object, snapshots.Object);
        var result = await resolver.ResolveAsync(UploadFolder);

        Assert.True(result.Success);
        Assert.Equal(excelDate, result.BusinessDate);
    }

    [Fact]
    public async Task ExcelDateEqualsPersistedKasa_FailsClosed_NoMutationDateReturned()
    {
        var sameDate = new DateOnly(2026, 8, 20);
        var dateRules = MockDateRules(sameDate, hasConflict: false, hasAny: true);
        var snapshots = MockSnapshots(new KasaRaporSnapshot { RaporTarihi = sameDate });

        var resolver = new ManualResolveWriteBusinessDateResolver(dateRules.Object, snapshots.Object);
        var result = await resolver.ResolveAsync(UploadFolder);

        Assert.False(result.Success);
        Assert.Null(result.BusinessDate);
        Assert.Equal(ManualResolveWriteBusinessDateFailureReason.ExcelDateEqualsPersistedKasa, result.FailureReason);
    }

    [Fact]
    public async Task ExcelDateBeforePersistedKasa_FailsClosed_StaleBackdating()
    {
        var persisted = new DateOnly(2026, 8, 20);
        var staleExcel = new DateOnly(2026, 8, 18);
        var dateRules = MockDateRules(staleExcel, hasConflict: false, hasAny: true);
        var snapshots = MockSnapshots(new KasaRaporSnapshot { RaporTarihi = persisted });

        var resolver = new ManualResolveWriteBusinessDateResolver(dateRules.Object, snapshots.Object);
        var result = await resolver.ResolveAsync(UploadFolder);

        Assert.False(result.Success);
        Assert.Null(result.BusinessDate);
        Assert.Equal(ManualResolveWriteBusinessDateFailureReason.ExcelDateBeforePersistedKasa, result.FailureReason);
    }

    [Fact]
    public async Task NoAnalyzableExcelDate_FailsClosed()
    {
        var dateRules = MockDateRules(proposedDate: null, hasConflict: false, hasAny: false);
        var snapshots = MockSnapshots(null);

        var resolver = new ManualResolveWriteBusinessDateResolver(dateRules.Object, snapshots.Object);
        var result = await resolver.ResolveAsync(UploadFolder);

        Assert.False(result.Success);
        Assert.Null(result.BusinessDate);
        Assert.Equal(ManualResolveWriteBusinessDateFailureReason.NoAnalyzableExcelDate, result.FailureReason);
    }

    [Fact]
    public async Task ConflictingExcelDate_RequiresUserDecision_FailsClosed()
    {
        // HasConflict=true → RequiresUserDecision=true even though ProposedDate is populated
        // (KasaReportDateRulesService proposes a "most frequent" date under conflict, but that is
        // not authoritative — it still requires user confirmation).
        var dateRules = MockDateRules(new DateOnly(2026, 8, 20), hasConflict: true, hasAny: true);
        var snapshots = MockSnapshots(null);

        var resolver = new ManualResolveWriteBusinessDateResolver(dateRules.Object, snapshots.Object);
        var result = await resolver.ResolveAsync(UploadFolder);

        Assert.False(result.Success);
        Assert.Null(result.BusinessDate);
        Assert.Equal(ManualResolveWriteBusinessDateFailureReason.ExcelDateConflict, result.FailureReason);
    }

    [Fact]
    public void NeverQueriesHesapKontrolRecords_OnlyExcelAndKasaSnapshotSources()
    {
        // Contract proof: the resolver's constructor only accepts date-rules + snapshot services —
        // there is no IBankaHesapKontrolService dependency available to query HesapKontrol records.
        var ctor = typeof(ManualResolveWriteBusinessDateResolver).GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType.Name).ToArray();

        Assert.Equal(new[] { "IKasaReportDateRulesService", "IKasaRaporSnapshotService" }, paramTypes);
    }

    private static Mock<IKasaReportDateRulesService> MockDateRules(
        DateOnly? proposedDate, bool hasConflict, bool hasAny)
    {
        var mock = new Mock<IKasaReportDateRulesService>();
        mock.Setup(x => x.EvaluateAsync(UploadFolder, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DateRulesEvaluation
            {
                ProposedDate = proposedDate,
                FinalSuggestedDate = proposedDate,
                HasConflict = hasConflict,
                HasAnyDate = hasAny
            });
        return mock;
    }

    private static Mock<IKasaRaporSnapshotService> MockSnapshots(KasaRaporSnapshot? lastGenelKasa)
    {
        var mock = new Mock<IKasaRaporSnapshotService>();
        mock.Setup(x => x.GetLastGenelKasaSnapshotBeforeOrOnAsync(DateOnly.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lastGenelKasa);
        return mock;
    }
}
