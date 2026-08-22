using System;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Infrastructure.Services;
using Moq;
using Xunit;

namespace KasaManager.Tests.Infrastructure;

/// <summary>
/// Helpy closure task 2 (smallest possible tests): Revision 3 Section 6'nın kesin sıra kontratını
/// (0 explicit/context → 1 son persisted Kasa → 2 analiz edilebilir Excel → 3 NoAnalyzableSource)
/// izole şekilde doğrular. Controller/DB bağımlılığı yok — yalnızca IKasaRaporSnapshotService ve
/// IKasaReportDateRulesService mock'lanır.
/// </summary>
public sealed class EffectiveAnalysisDateResolverTests
{
    private static readonly DateOnly ExplicitDate = new(2026, 8, 10);
    private static readonly DateOnly PersistedDate = new(2026, 8, 15);
    private static readonly DateOnly ExcelDate = new(2026, 8, 18);

    private static (Mock<IKasaRaporSnapshotService> snapshots, Mock<IKasaReportDateRulesService> dateRules)
        CreateMocks()
    {
        var snapshots = new Mock<IKasaRaporSnapshotService>();
        var dateRules = new Mock<IKasaReportDateRulesService>();
        // Varsayılan: hiçbir kaynak yok (Tier 3'e düşer) — testler yalnızca ilgili tier'ı override eder.
        snapshots
            .Setup(s => s.GetLastGenelKasaSnapshotBeforeOrOnAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((KasaRaporSnapshot?)null);
        dateRules
            .Setup(d => d.EvaluateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DateRulesEvaluation { ProposedDate = null });
        return (snapshots, dateRules);
    }

    [Fact]
    public async Task Tier0_ExplicitContextDate_TakesPrecedenceOverEverythingElse()
    {
        var (snapshots, dateRules) = CreateMocks();
        // Tier 1 ve Tier 2 de dolu olsa bile Tier 0 kazanmalı.
        snapshots
            .Setup(s => s.GetLastGenelKasaSnapshotBeforeOrOnAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KasaRaporSnapshot { RaporTarihi = PersistedDate });
        dateRules
            .Setup(d => d.EvaluateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DateRulesEvaluation { ProposedDate = ExcelDate });
        var resolver = new EffectiveAnalysisDateResolver(snapshots.Object, dateRules.Object);

        var result = await resolver.ResolveAsync(ExplicitDate, "Aksam", "C:\\fake", CancellationToken.None);

        Assert.Equal(ExplicitDate, result.Date);
        Assert.Equal(AnalysisDateSourceTier.ExplicitContext, result.Tier);
    }

    [Fact]
    public async Task Tier1_SuccessfulPersistedKasa_WinsWhenNoExplicitDate()
    {
        var (snapshots, dateRules) = CreateMocks();
        snapshots
            .Setup(s => s.GetLastGenelKasaSnapshotBeforeOrOnAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KasaRaporSnapshot { RaporTarihi = PersistedDate });
        dateRules
            .Setup(d => d.EvaluateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DateRulesEvaluation { ProposedDate = ExcelDate });
        var resolver = new EffectiveAnalysisDateResolver(snapshots.Object, dateRules.Object);

        var result = await resolver.ResolveAsync(null, "Aksam", "C:\\fake", CancellationToken.None);

        Assert.Equal(PersistedDate, result.Date);
        Assert.Equal(AnalysisDateSourceTier.SuccessfulPersistedKasa, result.Tier);
    }

    [Fact]
    public async Task Tier2_AnalyzableExcel_WinsWhenNoExplicitDateAndNoPersistedKasa()
    {
        var (snapshots, dateRules) = CreateMocks();
        dateRules
            .Setup(d => d.EvaluateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DateRulesEvaluation { ProposedDate = ExcelDate });
        var resolver = new EffectiveAnalysisDateResolver(snapshots.Object, dateRules.Object);

        var result = await resolver.ResolveAsync(null, "Aksam", "C:\\fake", CancellationToken.None);

        Assert.Equal(ExcelDate, result.Date);
        Assert.Equal(AnalysisDateSourceTier.AnalyzableExcel, result.Tier);
    }

    [Fact]
    public async Task Tier3_NoAnalyzableSource_WhenNothingResolves()
    {
        var (snapshots, dateRules) = CreateMocks();
        var resolver = new EffectiveAnalysisDateResolver(snapshots.Object, dateRules.Object);

        var result = await resolver.ResolveAsync(null, "Aksam", "C:\\fake", CancellationToken.None);

        Assert.Null(result.Date);
        Assert.Equal(AnalysisDateSourceTier.NoAnalyzableSource, result.Tier);
    }

    [Fact]
    public async Task ExplicitContextDate_DefaultValue_IsNotTreatedAsExplicit()
    {
        // default(DateOnly) = 0001-01-01 — sözdizimsel olarak "boş" kabul edilir, Tier 0'ı tetiklemez.
        var (snapshots, dateRules) = CreateMocks();
        dateRules
            .Setup(d => d.EvaluateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DateRulesEvaluation { ProposedDate = ExcelDate });
        var resolver = new EffectiveAnalysisDateResolver(snapshots.Object, dateRules.Object);

        var result = await resolver.ResolveAsync(default(DateOnly), "Aksam", "C:\\fake", CancellationToken.None);

        Assert.Equal(AnalysisDateSourceTier.AnalyzableExcel, result.Tier);
    }
}
