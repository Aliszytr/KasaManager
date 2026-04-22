using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using KasaManager.Application.Abstractions;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Domain.Reports;

namespace ParityRunner;

class Program
{
    static readonly string[] ParityKeys = new[]
    {
        "GenelKasa",
        "DevredenKasa",
        "BankayaYatirilacakNakit",
        "BankayaYatirilacakHarc",
        "BozukParaHaricKasa",
        "VergiKasa_SelectionTotal"
    };

    static async Task Main(string[] args)
    {
        Console.WriteLine("===============================================================");
        Console.WriteLine("  KasaYonetim PARITY TEST RUNNER (NEW ENGINE VS DB SNAPSHOTS)");
        Console.WriteLine("===============================================================");
        
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
            
        var config = configBuilder.Build();

        // Parametreler
        var startDateStr = config["Parity:StartDate"] ?? config["start"] ?? "2026-04-01";
        var endDateStr = config["Parity:EndDate"] ?? config["end"] ?? "2026-04-05";
        var startDate = DateOnly.Parse(startDateStr);
        var endDate = DateOnly.Parse(endDateStr);
        
        var uploadFolder = config["Parity:UploadFolder"] ?? @"H:\KasaYonetim_New\src\KasaManager.Web\wwwroot\Data\Raporlar";
        var outputDir = config["Parity:OutputDir"] ?? "./parity-results";
        var connectionString = config.GetConnectionString("KasaDb") ?? "Server=localhost;Database=KasaManager;Trusted_Connection=True;TrustServerCertificate=True";

        Directory.CreateDirectory(outputDir);

        // DI setup (yeni proje servisleri)
        var services = new ServiceCollection();
        
        // Loglama
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning); // Too much info will hide parity logs
        });
        
        // Infrastructure and Application
        services.AddDbContextFactory<KasaManagerDbContext>(options =>
            options.UseSqlServer(connectionString));
        services.AddDbContext<KasaManagerDbContext>(options =>
            options.UseSqlServer(connectionString));
            
        // Infrastructure and Web-like registrations
        KasaManager.Infrastructure.Persistence.PersistenceDependencyInjection.AddSnapshotPersistence(services, config, null!);
        KasaManager.Infrastructure.ProcessingDependencyInjection.AddProcessingWorkspaceInMemory(services);
        KasaManager.Infrastructure.PipelineServiceCollectionExtensions.AddFormulaPipeline(services);
        
        services.AddSingleton<KasaManager.Application.Abstractions.IAppCache, KasaManager.Infrastructure.Caching.MemoryAppCache>();
        services.AddScoped<KasaManager.Application.Abstractions.IExcelTableReader, KasaManager.Infrastructure.Excel.ExcelDataReaderTableReader>();
        services.AddScoped<KasaManager.Application.Services.ImportOrchestrator>();
        services.AddScoped<KasaManager.Application.Abstractions.IImportOrchestrator, KasaManager.Application.Services.CachingImportOrchestrator>();
        
        services.AddScoped<KasaManager.Application.Abstractions.IKasaReportDateRulesService, KasaManager.Application.Services.KasaReportDateRulesService>();
        services.AddScoped<KasaManager.Application.Abstractions.ICarryoverResolver, KasaManager.Infrastructure.Services.CarryoverResolver>();
        services.AddScoped<KasaManager.Application.Abstractions.IKasaDraftService, KasaManager.Application.Services.KasaDraftService>();
        services.AddScoped<KasaManager.Application.Abstractions.IEksikFazlaProjectionEngine, KasaManager.Application.Services.EksikFazlaProjectionEngine>();
        services.AddScoped<KasaManager.Application.Observability.IAlertService, KasaManager.Application.Observability.AlertService>();
        
        services.AddScoped<KasaManager.Application.Abstractions.IFormulaEngineService, KasaManager.Application.Services.FormulaEngineService>();
        services.AddScoped<KasaManager.Application.Abstractions.IExcelValidationService, KasaManager.Application.Services.Import.ExcelValidationService>();
        services.AddScoped<KasaManager.Application.Abstractions.IBankaHesapKontrolService, KasaManager.Infrastructure.Services.BankaHesapKontrolService>();
        services.AddScoped<KasaManager.Application.Abstractions.IFinansalIstisnaService, KasaManager.Infrastructure.Services.FinansalIstisnaService>();
        services.AddScoped<KasaManager.Application.Orchestration.IKasaOrchestrator, KasaManager.Application.Orchestration.KasaOrchestrator>();
        services.AddScoped<KasaManager.Application.Abstractions.IGenelKasaRaporService, KasaManager.Application.Services.GenelKasaRaporService>();

        services.Configure<KasaManager.Domain.Settings.UstRaporSourceOptions>(
             config.GetSection(KasaManager.Domain.Settings.UstRaporSourceOptions.SectionName));
        // ParityCheck, DataFirst, ReadAdapter (FAZ 4 / FAZ 12)
        services.AddScoped<KasaManager.Application.Services.DataFirst.IFactNormalizationService, KasaManager.Infrastructure.Services.FactNormalizationService>();
        services.AddScoped<KasaManager.Application.Services.DataFirst.IParityCheckService, KasaManager.Infrastructure.Services.ParityCheckService>();
        services.AddScoped<KasaManager.Application.Services.DataFirst.IDataFirstTrustReadRepository, KasaManager.Infrastructure.Services.DataFirstTrustReadRepository>();
        services.AddScoped<KasaManager.Application.Services.DataFirst.IDataFirstTrustService, KasaManager.Infrastructure.Services.DataFirstTrustService>();

        services.AddScoped<KasaManager.Application.Services.ReadAdapter.ILegacyKasaReadService, KasaManager.Infrastructure.Services.ReadAdapter.LegacyKasaReadService>();
        services.AddScoped<KasaManager.Application.Services.ReadAdapter.IDataFirstKasaReadService, KasaManager.Infrastructure.Services.ReadAdapter.DataFirstKasaReadService>();
        services.AddScoped<KasaManager.Application.Services.ReadAdapter.IReadModeResolver, KasaManager.Infrastructure.Services.ReadAdapter.ReadModeResolver>();
        services.AddScoped<KasaManager.Application.Services.ReadAdapter.IReadEligibilityService, KasaManager.Infrastructure.Services.ReadAdapter.ReadEligibilityService>();
        services.AddScoped<KasaManager.Application.Services.ReadAdapter.IKasaReadModelService, KasaManager.Infrastructure.Services.ReadAdapter.KasaReadModelService>();
        services.AddScoped<KasaManager.Application.Services.ReadAdapter.IDualKasaReadService, KasaManager.Infrastructure.Services.ReadAdapter.DualKasaReadService>();
        
        services.AddScoped<KasaManager.Application.Services.DataFirst.ISwitchSimulationService, KasaManager.Infrastructure.Services.SwitchSimulationService>();
        services.AddScoped<KasaManager.Application.Services.DataFirst.ISwitchReadinessPolicyService, KasaManager.Infrastructure.Services.SwitchReadinessPolicyService>();
        services.AddScoped<KasaManager.Application.Services.DataFirst.ISwitchGateService, KasaManager.Infrastructure.Services.SwitchGateService>();
        services.AddScoped<KasaManager.Application.Services.DataFirst.IManualSwitchOrchestrator, KasaManager.Infrastructure.Services.ManualSwitchOrchestrator>();
        
        // KasaValidation Service
        services.AddScoped<KasaManager.Application.Abstractions.IKasaValidationService, KasaManager.Infrastructure.Services.KasaValidationService>();
        
        // Comparison Service
        services.AddSingleton<KasaManager.Application.Services.Comparison.BankaAciklamaParser>();
        services.AddScoped<KasaManager.Application.Abstractions.IComparisonService, KasaManager.Application.Services.Comparison.ComparisonService>();
        services.AddScoped<KasaManager.Application.Abstractions.IComparisonExportService, KasaManager.Infrastructure.Export.ComparisonExportService>();
        services.AddScoped<KasaManager.Application.Abstractions.IComparisonDecisionService, KasaManager.Infrastructure.Services.ComparisonDecisionService>();
        services.AddScoped<KasaManager.Application.Abstractions.IComparisonArchiveService, KasaManager.Application.Services.Comparison.ComparisonArchiveService>();

        // VergideBirikenLedger Service
        services.AddScoped<KasaManager.Application.Abstractions.IVergideBirikenLedgerService, KasaManager.Infrastructure.Services.VergideBirikenLedgerService>();
        
        // Settings Options (SwitchPolicy handled already)
        services.Configure<KasaManager.Domain.Settings.SwitchPolicyOptions>(
             config.GetSection(KasaManager.Domain.Settings.SwitchPolicyOptions.SectionName));

        // Disable file storage dependencies by mocking or minimal implementation
        // KasaDraftService requires these:
        services.AddScoped<KasaManager.Application.Abstractions.IFileStorage, KasaManager.Infrastructure.Storage.FileStorage>(sp => 
            new KasaManager.Infrastructure.Storage.FileStorage(Directory.GetCurrentDirectory()));
            
        // LegacyKasaDb (since ILegacyKasaReadService needs it)
        services.AddDbContext<KasaManager.Infrastructure.Legacy.LegacyKasaDbContext>(options =>
            options.UseSqlServer(config.GetConnectionString("LegacyKasaConnection") ?? "Server=.;Database=KasaRaporuDB;Trusted_Connection=True;TrustServerCertificate=True")
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddScoped<KasaManager.Application.Abstractions.ILegacyKasaService, KasaManager.Infrastructure.Legacy.LegacyKasaService>();


        var sp = services.BuildServiceProvider();
        var newEngine = new NewEngineWrapper(sp);
        var allResults = new List<(DateOnly Date, string Type, List<DiffEngine.DiffResult> Diffs)>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            foreach (var hesapTuru in new[] { "Sabah", "Aksam" })
            {
                Console.WriteLine($"\n[TEST] {date:yyyy-MM-dd} {hesapTuru}...");
                
                // Yeni projeden (hem Legacy hem Yeni sonucu içeren KasaDraftResult'u döner)
                var combinedDict = await newEngine.ComputeAsync(date, hesapTuru, uploadFolder, CancellationToken.None);

                var expectedSonuclar = new Dictionary<string, decimal>();
                expectedSonuclar["GenelKasa"] = combinedDict.GetValueOrDefault("LEGACY_genel_kasa", 0m);
                expectedSonuclar["DevredenKasa"] = combinedDict.GetValueOrDefault("dunden_devreden_kasa", 0m);
                expectedSonuclar["BankayaYatirilacakNakit"] = combinedDict.GetValueOrDefault("LEGACY_bankaya_yatirilacak_tahsilat", 0m);
                expectedSonuclar["BankayaYatirilacakTahsilat"] = combinedDict.GetValueOrDefault("LEGACY_bankaya_yatirilacak_tahsilat", 0m);
                expectedSonuclar["BankayaYatirilacakHarc"] = combinedDict.GetValueOrDefault("LEGACY_bankaya_yatirilacak_harc", 0m);
                expectedSonuclar["BozukParaHaricKasa"] = combinedDict.GetValueOrDefault("LEGACY_bozuk_para_haric_kasa", 0m);
                expectedSonuclar["VergiKasa_SelectionTotal"] = combinedDict.GetValueOrDefault("vergi_bina_kasa", combinedDict.GetValueOrDefault("vergi_kasa_bakiye_toplam", 0m));
                
                var expected = new ParitySnapshot { Tarih = date, HesapTuru = hesapTuru, Sonuclar = expectedSonuclar };
                
                var actualSonuclar = new Dictionary<string, decimal>();
                actualSonuclar["GenelKasa"] = combinedDict.GetValueOrDefault("genel_kasa", 0m);
                actualSonuclar["DevredenKasa"] = combinedDict.GetValueOrDefault("dunden_devreden_kasa", 0m);
                actualSonuclar["BankayaYatirilacakNakit"] = combinedDict.GetValueOrDefault("bankaya_yatirilacak_tahsilat", 0m);
                actualSonuclar["BankayaYatirilacakTahsilat"] = combinedDict.GetValueOrDefault("bankaya_yatirilacak_tahsilat", 0m);
                actualSonuclar["BankayaYatirilacakHarc"] = combinedDict.GetValueOrDefault("bankaya_yatirilacak_harc", 0m);
                actualSonuclar["BozukParaHaricKasa"] = combinedDict.GetValueOrDefault("bozuk_para_haric_kasa", 0m);
                actualSonuclar["VergiKasa_SelectionTotal"] = combinedDict.GetValueOrDefault("vergi_bina_kasa", combinedDict.GetValueOrDefault("vergi_kasa_bakiye_toplam", 0m));
                
                var actual = new ParitySnapshot { Tarih = date, HesapTuru = hesapTuru, Sonuclar = actualSonuclar };

                // 3. Karsilastir
                var diffs = DiffEngine.Compare(expected, actual, tolerance: 0.01m);
                allResults.Add((date, hesapTuru, diffs));
                
                // 4. JSON kaydet
                var fileName = $"{date:yyyy-MM-dd}_{hesapTuru.ToLower()}";
                File.WriteAllText($"{outputDir}/{fileName}_expected.json", 
                    JsonSerializer.Serialize(expected, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText($"{outputDir}/{fileName}_actual.json",
                    JsonSerializer.Serialize(actual, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText($"{outputDir}/{fileName}_full.json",
                    JsonSerializer.Serialize(combinedDict, new JsonSerializerOptions { WriteIndented = true }));
                    
                // Satir ici ufak diff bas
                var mismatches = diffs.Where(d => !d.IsMatch && ParityKeys.Contains(d.Key)).ToList();
                if(mismatches.Any()) {
                     Console.ForegroundColor = ConsoleColor.Yellow;
                     Console.WriteLine($"       -> Bulunan Fark(lar): {mismatches.Count}");
                     Console.ResetColor();
                } else {
                     Console.ForegroundColor = ConsoleColor.Green;
                     Console.WriteLine($"       -> TUM DEGERLER ESIT!");
                     Console.ResetColor();
                }
            }
        }
        
        await KanitTests.RunAsync(sp, uploadFolder);

        // 5. Ozet rapor
        PrintSummary(allResults);
    }
    
    static void PrintSummary(List<(DateOnly Date, string Type, List<DiffEngine.DiffResult> Diffs)> allResults)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  KasaYonetim PARİTY TEST RAPORU — {DateTime.Now:yyyy-MM-dd}");
        Console.WriteLine("═══════════════════════════════════════════════════════════════\n");
        
        int passedTests = 0;
        
        foreach (var (date, type, diffs) in allResults)
        {
            Console.WriteLine($"📅 {date:yyyy-MM-dd} {type}");
            var targetKeys = ParityKeys.ToList();
            int matched = 0;
            
            foreach(var key in targetKeys) 
            {
                 var d = diffs.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                 if (d == null) continue;
                 
                 if (d.IsMatch)
                 {
                     Console.ForegroundColor = ConsoleColor.Green;
                     Console.WriteLine($"  ✅ {d.Key,-25} {d.Expected,12:N2} = {d.Actual,12:N2}");
                     matched++;
                 }
                 else
                 {
                     Console.ForegroundColor = ConsoleColor.Red;
                     Console.WriteLine($"  ❌ {d.Key,-25} {d.Expected,12:N2} ≠ {d.Actual,12:N2} (FARK: {d.Difference:N2})");
                 }
                 Console.ResetColor();
            }
            
            if (matched == targetKeys.Count) 
            {
                 Console.WriteLine($"  SONUÇ: {matched}/{targetKeys.Count} alan eşleşti ✅\n");
                 passedTests++;
            }
            else
            {
                 Console.WriteLine($"  SONUÇ: {matched}/{targetKeys.Count} alan eşleşti ⚠️\n");
                 
                 // Show mismatched fields specifically for easier debugging
                 var failingKeys = diffs.Where(x => !x.IsMatch).ToList();
                 if(failingKeys.Count > 0){
                      Console.WriteLine($"  DİĞER UYUMSUZ ALANLAR =============================================");
                      foreach(var failed in failingKeys){
                          if(!targetKeys.Contains(failed.Key)){
                              Console.ForegroundColor = ConsoleColor.Magenta;
                              Console.WriteLine($"  {failed.Key,-25} EXPECT: {failed.Expected,12:N2} ACTUAL: {failed.Actual,12:N2} DIFF: {failed.Difference:N2}");
                          }
                      }
                      Console.ResetColor();
                 }
                 Console.WriteLine("");
            }
        }
        
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  GENEL SONUÇ: {passedTests}/{allResults.Count} test geçti");
        Console.WriteLine($"  FARK TESPİT EDİLEN: {allResults.Count - passedTests} test");
        Console.WriteLine($"\n  ⚠️ Detaylar: ./parity-results/ dizininde JSON dosyaları");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
    }
}
