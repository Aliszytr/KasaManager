using KasaManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace KasaManager.Web.Controllers;

/// <summary>
/// Veritabanı yedekleme controller'ı.
/// SQLite: dosyayı kopyalayıp indir.
/// SQL Server: BACKUP DATABASE komutu ile .bak dosyası oluşturup indir.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed class BackupController : Controller
{
    private readonly KasaManagerDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<BackupController> _logger;

    public BackupController(KasaManagerDbContext db, IConfiguration cfg,
        IWebHostEnvironment env, ILogger<BackupController> logger)
    {
        _db = db;
        _cfg = cfg;
        _env = env;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var provider = (_cfg["Database:Provider"] ?? "SqlServer").Trim();
        var isSqlite = provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase);

        ViewBag.Provider = provider;
        ViewBag.IsSqlite = isSqlite;

        if (isSqlite)
        {
            var relativePath = (_cfg["Database:SqlitePath"] ?? "App_Data/KasaManager.db").Trim();
            var dbPath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(_env.ContentRootPath, relativePath);
            ViewBag.DbPath = dbPath;
            ViewBag.DbExists = System.IO.File.Exists(dbPath);
            if (ViewBag.DbExists)
            {
                var fi = new FileInfo(dbPath);
                ViewBag.DbSize = $"{fi.Length / 1024.0:N0} KB";
                ViewBag.DbModified = fi.LastWriteTime.ToString("dd.MM.yyyy HH:mm:ss");
            }
        }
        else
        {
            // SQL Server bilgilerini parse et
            var connStr = _cfg.GetConnectionString("SqlConnection") ?? "";
            try
            {
                var builder = new SqlConnectionStringBuilder(connStr);
                ViewBag.ServerName = builder.DataSource;
                ViewBag.DatabaseName = builder.InitialCatalog;
                ViewBag.DbPath = $"{builder.DataSource} → {builder.InitialCatalog}";
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "SQL Server connection string parse edilemedi");
                ViewBag.ServerName = "?";
                ViewBag.DatabaseName = "?";
                ViewBag.DbPath = connStr.Length > 50 ? connStr[..50] + "..." : connStr;
            }

            // DB boyutunu sorgula
            try
            {
                var conn = _db.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT SUM(size * 8 / 1024) FROM sys.database_files";
                var result = cmd.ExecuteScalar();
                ViewBag.DbSize = result is not null ? $"{result} MB" : "—";
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "SQL Server DB boyutu sorgulanamadı");
                ViewBag.DbSize = "—";
            }
        }

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadBackup(CancellationToken ct)
    {
        var provider = (_cfg["Database:Provider"] ?? "SqlServer").Trim();

        if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            return await DownloadSqliteBackup(ct);

        return await DownloadSqlServerBackup(ct);
    }

    // ────────────── SQL Server ──────────────

    private async Task<IActionResult> DownloadSqlServerBackup(CancellationToken ct)
    {
        var connStr = _cfg.GetConnectionString("SqlConnection") ?? "";
        string dbName;
        try
        {
            var builder = new SqlConnectionStringBuilder(connStr);
            dbName = builder.InitialCatalog;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SQL Server connection string parse edilemedi");
            TempData["Error"] = "Connection string parse edilemedi.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(dbName))
        {
            TempData["Error"] = "Connection string'de Database adı bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"KasaManager_Backup_{timestamp}.bak";

        try
        {
            // ADO.NET ile direkt bağlantı (EF Core BACKUP komutuyla sorun çıkarabiliyor)
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(ct);

            // SQL Server'ın yazma yetkisi olan dizini bul
            var backupDir = await GetSqlServerDirectoryAsync(conn, ct);
            var backupPath = Path.Combine(backupDir, backupFileName);

            _logger.LogInformation("SQL Server yedekleme başlatılıyor: {Database} → {Path}", dbName, backupPath);

            // BACKUP DATABASE
            var sql = $"BACKUP DATABASE [{dbName}] TO DISK = N'{backupPath}' WITH FORMAT, INIT, COMPRESSION, NAME = N'KasaManager Full Backup'";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.CommandTimeout = 300;

            // Bilgi mesajlarını logla
            conn.InfoMessage += (_, e) => _logger.LogDebug("SQL Backup: {Message}", e.Message);

            await cmd.ExecuteNonQueryAsync(ct);

            // Dosya kontrolü
            if (!System.IO.File.Exists(backupPath))
            {
                TempData["Error"] = $"Yedek dosyası oluşturulamadı. Hedef dizin: {backupDir}";
                return RedirectToAction(nameof(Index));
            }

            var fileSize = new FileInfo(backupPath).Length;
            _logger.LogInformation("SQL Server yedekleme tamamlandı: {Path} ({Size:N0} bytes)", backupPath, fileSize);

            // Dosyayı oku ve indir
            var bytes = await System.IO.File.ReadAllBytesAsync(backupPath, ct);
            try { System.IO.File.Delete(backupPath); }
            catch (IOException ex) { _logger.LogDebug(ex, "Geçici backup dosyası silinemedi: {Path}", backupPath); }

            // İndirme tamamlandı — JS tarafında butonu sıfırlamak için cookie set et
            Response.Cookies.Append("KasaBackupComplete", "1", new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromMinutes(1),
                HttpOnly = false  // JS okuması gerekli
            });

            return File(bytes, "application/octet-stream", backupFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL Server yedekleme hatası");
            TempData["Error"] = $"Yedekleme hatası: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }

    /// <summary>
    /// SQL Server servisi ve web uygulamasının ikisinin de erişebildiği
    /// ortak bir dizin döndürür. C:\Users\Public tüm kullanıcılar ve
    /// servis hesapları tarafından okunabilir/yazılabilir.
    /// </summary>
    private Task<string> GetSqlServerDirectoryAsync(SqlConnection conn, CancellationToken ct)
    {
        // C:\Users\Public\KasaBackups — hem SQL Server hem web app erişebilir
        var publicDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
        if (string.IsNullOrWhiteSpace(publicDir))
            publicDir = @"C:\Users\Public\Documents";

        var backupDir = Path.Combine(publicDir, "KasaBackups");
        Directory.CreateDirectory(backupDir);

        _logger.LogInformation("Backup dizini: {Dir}", backupDir);
        return Task.FromResult(backupDir);
    }

    // ────────────── SQLite ──────────────

    private async Task<IActionResult> DownloadSqliteBackup(CancellationToken ct)
    {
        var relativePath = (_cfg["Database:SqlitePath"] ?? "App_Data/KasaManager.db").Trim();
        var dbPath = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(_env.ContentRootPath, relativePath);

        if (!System.IO.File.Exists(dbPath))
        {
            TempData["Error"] = "Veritabanı dosyası bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        // WAL checkpoint
        try
        {
            await _db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", ct);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "SQLite WAL checkpoint başarısız"); }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var tempPath = Path.Combine(Path.GetTempPath(), $"KasaManager_Backup_{timestamp}.db");

        try
        {
            System.IO.File.Copy(dbPath, tempPath, overwrite: true);
            _logger.LogInformation("SQLite yedek oluşturuldu: {Path} ({Size} bytes)", tempPath, new FileInfo(tempPath).Length);

            var bytes = await System.IO.File.ReadAllBytesAsync(tempPath, ct);
            try { System.IO.File.Delete(tempPath); }
            catch (IOException ex) { _logger.LogDebug(ex, "Geçici SQLite backup dosyası silinemedi: {Path}", tempPath); }

            // İndirme tamamlandı — JS tarafında butonu sıfırlamak için cookie set et
            Response.Cookies.Append("KasaBackupComplete", "1", new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromMinutes(1),
                HttpOnly = false  // JS okuması gerekli
            });

            return File(bytes, "application/octet-stream", $"KasaManager_Backup_{timestamp}.db");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQLite yedekleme hatası");
            TempData["Error"] = $"Yedekleme hatası: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }
}
