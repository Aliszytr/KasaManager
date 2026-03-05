using KasaManager.Domain.Abstractions;

namespace KasaManager.Application.Abstractions;

public interface IFileStorage
{
    Result<string> SaveUploadedFile(
        Stream fileStream,
        string originalFileName,
        string subFolder,
        string[] allowedExtensions,
        long maxBytes,
        bool overwrite);

    /// <summary>
    /// Returns file names (not full paths) inside the given subFolder under wwwroot.
    /// Folder is created if it does not exist.
    /// </summary>
    IReadOnlyList<string> ListFiles(string subFolder);
}
