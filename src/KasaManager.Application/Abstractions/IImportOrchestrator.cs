using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Abstractions;

public interface IImportOrchestrator
{
    Result<ImportedTable> Import(string absoluteFilePath, ImportFileKind kind);

    /// <summary>
    /// True Source okuma: satır limiti yok (MasrafveReddiyat gibi büyük dosyalar için).
    /// </summary>
    Result<ImportedTable> ImportTrueSource(string absoluteFilePath, ImportFileKind kind);
    ImportFileKind GuessKindFromFileName(string fileName);
}
