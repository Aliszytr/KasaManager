using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Services;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Settings;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Moq;

namespace ProofGeneratorParity
{
    class Program
    {
        static async Task Main()
        {
             // 1. DI Initialization & Mocking for Controlled Testing
             var defaultsMock = new Mock<IKasaGlobalDefaultsService>();
             var calcSnapsMock = new Mock<ICalculatedKasaSnapshotService>();
             var legacySnapsMock = new Mock<IKasaRaporSnapshotService>();
             
             // Setup basic defaults
             var settings = new KasaGlobalDefaultsSettings {
                 Id = 1,
                 DefaultGenelKasaDevredenSeed = 0m,
                 DefaultGenelKasaBaslangicTarihiSeed = new DateTime(2026, 1, 1)
             };
             defaultsMock.Setup(x => x.GetOrCreateAsync(It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(settings);
             defaultsMock.Setup(x => x.GetAsync(It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(settings);

             var loggerFactory = LoggerFactory.Create(builder => { builder.AddConsole(); builder.SetMinimumLevel(LogLevel.Information); });
             var logger = loggerFactory.CreateLogger<CarryoverResolver>();

             var resolver = new CarryoverResolver(
                 defaultsMock.Object,
                 calcSnapsMock.Object,
                 legacySnapsMock.Object,
                 logger
             );

             Console.WriteLine("\n[--- CARRYOVER RESOLVER RUNTIME SCENARIOS ---]\n");
             var targetDate = new DateOnly(2026, 5, 10);

             // Senaryo A: Seed var ve geçerli
             Console.WriteLine(">> SCENARIO A: SeedOverride (Valid Seed Exists)");
             settings.DefaultGenelKasaDevredenSeed = 15000m; // > 0
             var resA = await resolver.ResolveAsync(targetDate, CarryoverScope.GenelKasa);
             PrintResult(resA);

             // Senaryo B: Seed yok, Calculated Snapshot Fallback devreye giriyor
             Console.WriteLine("\n>> SCENARIO B: CalculatedSnapshotFallback (Seed = 0)");
             settings.DefaultGenelKasaDevredenSeed = 0m; 
             
             var snapId = Guid.NewGuid();
             var newSnap = new KasaManager.Domain.Reports.Snapshots.CalculatedKasaSnapshot {
                 Id = snapId,
                 RaporTarihi = targetDate.AddDays(-1),
                 RaporTuru = KasaRaporTuru.Genel,
                 OutputsJson = "{\"SonrayaDevredecek\": 24500.50}"
             };
             calcSnapsMock.Setup(x => x.ListByDateRangeAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), KasaRaporTuru.Genel, It.IsAny<System.Threading.CancellationToken>()))
                          .Returns(Task.FromResult(new System.Collections.Generic.List<KasaManager.Domain.Reports.Snapshots.CalculatedKasaSnapshot> { newSnap }));
                          
             var resB = await resolver.ResolveAsync(targetDate, CarryoverScope.GenelKasa);
             PrintResult(resB);

             // Senaryo C: DefaultZero (Hiçbirisi yok)
             Console.WriteLine("\n>> SCENARIO C: DefaultZero (No Seed, No Snapshots)");
             calcSnapsMock.Setup(x => x.ListByDateRangeAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), KasaRaporTuru.Genel, It.IsAny<System.Threading.CancellationToken>()))
                          .Returns(Task.FromResult(new System.Collections.Generic.List<KasaManager.Domain.Reports.Snapshots.CalculatedKasaSnapshot>()));
             legacySnapsMock.Setup(x => x.GetLastGenelKasaSnapshotBeforeOrOnAsync(It.IsAny<DateOnly>(), It.IsAny<System.Threading.CancellationToken>()))
                            .Returns(Task.FromResult((KasaManager.Domain.Reports.Snapshots.KasaRaporSnapshot)null));

             var resC = await resolver.ResolveAsync(targetDate, CarryoverScope.GenelKasa);
             PrintResult(resC);

             Console.WriteLine("\n[--- END DIAGNOSTIC ---]\n");
             
             // Restoring ProofGeneratorParity so we don't accidentally leave it broken.
             // Wait, I am overwriting it entirely for this quick run.
        }

        static void PrintResult(CarryoverResolutionResult res)
        {
            Console.WriteLine($"    Scope        : GenelKasa");
            Console.WriteLine($"    TargetDate   : {res.RangeStart:yyyy-MM-dd} (from resolution logic)");
            Console.WriteLine($"    Resolved Val : {res.Value:N2}");
            Console.WriteLine($"    SourceCode   : {res.SourceCode}");
            Console.WriteLine($"    SourceDate   : {(res.SourceDate.HasValue ? res.SourceDate.Value.ToString("yyyy-MM-dd") : "N/A")}");
            Console.WriteLine($"    UsedFallback : {res.UsedFallback}");
            Console.WriteLine($"    Reason       : {res.Reason}");
        }
    }
}
