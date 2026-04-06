using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using KasaManager.Application.Services.DataFirst;

namespace KasaManager.Application.Services;

public sealed class ImportOrchestrator : IImportOrchestrator
{
    private readonly IExcelTableReader _excel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportOrchestrator> _logger;

    public ImportOrchestrator(IExcelTableReader excel, IServiceScopeFactory scopeFactory, ILogger<ImportOrchestrator> logger)
    {
        _excel = excel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    

    public Result<ImportedTable> ImportTrueSource(string absoluteFilePath, ImportFileKind kind)
    {
        if (string.IsNullOrWhiteSpace(absoluteFilePath))
            return Result<ImportedTable>.Fail("Dosya yolu boş.");

        if (!File.Exists(absoluteFilePath))
            return Result<ImportedTable>.Fail($"Dosya bulunamadı: {absoluteFilePath}");

        var fileName = Path.GetFileName(absoluteFilePath);

        if (kind == ImportFileKind.Unknown)
            kind = GuessKindFromFileName(fileName);

        var options = ImportProfiles.GetTrueSourceOptions(kind);

        var readResult = _excel.ReadTable(absoluteFilePath, options);

        if (!readResult.Ok || readResult.Value is null)
            return Result<ImportedTable>.Fail(readResult.Error ?? "Excel okuma başarısız.");

        var table = readResult.Value;

        var imported = new ImportedTable
        {
            SourceFileName = fileName,
            Kind = kind,
            Columns = table.Columns,
            ColumnMetas = table.ColumnMetas,
            Rows = table.Rows
        };

        // Faz 1: Shadow Ingestion (Fire and Forget)
        FireAndForgetShadowIngest(imported, absoluteFilePath);

        return Result<ImportedTable>.Success(imported);
    }

public ImportFileKind GuessKindFromFileName(string fileName)
    {
        var n = (fileName ?? string.Empty).ToLowerInvariant();

        if (n.Contains("bankahar") || (n.Contains("banka") && n.Contains("harc")))
            return ImportFileKind.BankaHarcama;

        if (n.Contains("bankatah") || (n.Contains("banka") && n.Contains("tah")))
            return ImportFileKind.BankaTahsilat;

        if (n.Contains("kasau") || n.Contains("kasa_ust") || n.Contains("kasaust"))
            return ImportFileKind.KasaUstRapor;

        if (n.Contains("masraf") || n.Contains("reddiyat") || n.Contains("masrafveredd"))
            return ImportFileKind.MasrafVeReddiyat;

        if (n.Contains("online") && n.Contains("harc"))
            return ImportFileKind.OnlineHarcama;

        if (n.Contains("online") && n.Contains("masraf"))
            return ImportFileKind.OnlineMasraf;

        if (n.Contains("online") && n.Contains("redd"))
            return ImportFileKind.OnlineReddiyat;

        return ImportFileKind.Unknown;
    }

    public Result<ImportedTable> Import(string absoluteFilePath, ImportFileKind kind)
    {
        if (string.IsNullOrWhiteSpace(absoluteFilePath))
            return Result<ImportedTable>.Fail("Dosya yolu boş.");

        if (!File.Exists(absoluteFilePath))
            return Result<ImportedTable>.Fail($"Dosya bulunamadı: {absoluteFilePath}");

        var fileName = Path.GetFileName(absoluteFilePath);

        if (kind == ImportFileKind.Unknown)
            kind = GuessKindFromFileName(fileName);

        var options = ImportProfiles.GetOptions(kind);

        // 1️⃣ Excel oku (R2 → ImportedTable döner)
        var readResult = _excel.ReadTable(absoluteFilePath, options);

        if (!readResult.Ok || readResult.Value is null)
            return Result<ImportedTable>.Fail(readResult.Error ?? "Excel okuma başarısız.");

        // 2️⃣ ImportedTable hazır gelir
        var table = readResult.Value;

        // 3️⃣ Orchestrator sadece bağlamsal bilgileri set eder
        var imported = new ImportedTable
        {
            SourceFileName = fileName,
            Kind = kind,
            Columns = table.Columns,           // R1 uyumluluk (istersen sonra kaldırırız)
            ColumnMetas = table.ColumnMetas,   // ✅ R2 asıl veri
            Rows = table.Rows
        };

        // Faz 1: Shadow Ingestion (Fire and Forget)
        FireAndForgetShadowIngest(imported, absoluteFilePath);

        return Result<ImportedTable>.Success(imported);
    }
    
    private void FireAndForgetShadowIngest(ImportedTable table, string absoluteFilePath)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var normalizationService = scope.ServiceProvider.GetRequiredService<IFactNormalizationService>();
                
                // Tarihi yoldan tahmin etmeye çalış, bulamazsan bugünü at.
                var targetDate = GuessDateFromPath(absoluteFilePath);
                
                await normalizationService.NormalizeAndSaveShadowFactsAsync(table, targetDate, absoluteFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FireAndForgetShadowIngest failed for {FilePath}", absoluteFilePath);
            }
        });
    }

    private DateOnly GuessDateFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return DateOnly.FromDateTime(DateTime.UtcNow);
        
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var p in parts.Reverse())
        {
            if (DateOnly.TryParseExact(p, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)) return d;
            if (DateOnly.TryParseExact(p, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d2)) return d2;
        }
        return DateOnly.FromDateTime(DateTime.UtcNow); 
    }
}
