using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Moq;
using Microsoft.Extensions.Logging;
using KasaManager.Infrastructure.Services;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Settings;
using KasaManager.Domain.Reports;

namespace KasaManager.Tests.Application;

public class CarryoverScenariosRunner
{
    private readonly ITestOutputHelper _output;

    public CarryoverScenariosRunner(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task RunAllScenarios()
    {
        var defaultsMock = new Mock<IKasaGlobalDefaultsService>();

        var settings = new KasaGlobalDefaultsSettings
        {
            Id = 1,
            DefaultGenelKasaDevredenSeed = 0m,
            DefaultGenelKasaBaslangicTarihiSeed = new DateTime(2026, 1, 1)
        };
        defaultsMock.Setup(x => x.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        
        var loggerMock = new Mock<ILogger<CarryoverResolver>>();

        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase(databaseName: "CarryoverScenariosDb_" + Guid.NewGuid().ToString())
            .Options;
        var dbContext = new KasaManagerDbContext(options);

        var resolver = new CarryoverResolver(
            defaultsMock.Object,
            dbContext,
            loggerMock.Object);

        var targetDate = new DateOnly(2026, 5, 10);

        _output.WriteLine("");
        _output.WriteLine("[--- CARRYOVER RESOLVER RUNTIME SCENARIOS ---]");
        _output.WriteLine("");

        // A) Seed var ve geçerli
        _output.WriteLine(">> SCENARIO A: SeedOverride (Valid Seed Exists)");
        settings.DefaultGenelKasaDevredenSeed = 15000m; // > 0
        var resA = await resolver.ResolveAsync(targetDate, CarryoverScope.GenelKasa);
        PrintResult(resA);

        // Snapshot okuma iptal (Stateless Mode - P4.2)
        // Dolayısıyla B ve C senaryoları birleşerek sadece Seed yoksa DefaultZero (0) döner.
        _output.WriteLine(">> SCENARIO B: DefaultZero (Seed = 0)");
        settings.DefaultGenelKasaDevredenSeed = 0m; 

        var resB = await resolver.ResolveAsync(targetDate, CarryoverScope.GenelKasa);
        PrintResult(resB);
        
        _output.WriteLine("[--- END DIAGNOSTIC ---]");
    }

    private void PrintResult(CarryoverResolutionResult res)
    {
        _output.WriteLine($"    target date   : 2026-05-10");
        _output.WriteLine($"    scope         : GenelKasa");
        _output.WriteLine($"    resolved value: {res.Value:N2}");
        _output.WriteLine($"    source code   : {res.SourceCode}");
        _output.WriteLine($"    used fallback : {res.UsedFallback}");
        if (res.SourceDate.HasValue)
            _output.WriteLine($"    source date   : {res.SourceDate.Value:yyyy-MM-dd}");
        _output.WriteLine($"    reason        : {res.Reason}");
        _output.WriteLine("");
    }
}
