using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services;

public sealed class ImportOrchestrator : IImportOrchestrator
{
    private readonly IExcelTableReader _excel;

    public ImportOrchestrator(IExcelTableReader excel)
    {
        _excel = excel;
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

        return Result<ImportedTable>.Success(imported);
    }
}
