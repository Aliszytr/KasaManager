using KasaManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Web.Controllers;

/// <summary>
/// MS8: Production health check endpoint.
/// DB bağlantısı, dosya sistemi erişimi ve disk alanı kontrolü.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("[controller]")]
public sealed class HealthController : ControllerBase
{
    private readonly KasaManagerDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _cfg;

    public HealthController(KasaManagerDbContext db, IWebHostEnvironment env, IConfiguration cfg)
    {
        _db = db;
        _env = env;
        _cfg = cfg;
    }

    /// <summary>
    /// GET /health — JSON health check döner.
    /// AllowAnonymous: monitoring araçları login olmadan kontrol edebilir.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var checks = new Dictionary<string, HealthCheckResult>();
        var overallHealthy = true;

        // ── 1. DB Bağlantısı ──
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            checks["database"] = new HealthCheckResult
            {
                Status = canConnect ? "Healthy" : "Unhealthy",
                Details = new
                {
                    Provider = _db.Database.ProviderName ?? "?",
                    DataSource = _db.Database.GetDbConnection().DataSource,
                    Database = _db.Database.GetDbConnection().Database
                }
            };
            if (!canConnect) overallHealthy = false;
        }
        catch (Exception ex)
        {
            overallHealthy = false;
            checks["database"] = new HealthCheckResult
            {
                Status = "Unhealthy",
                Details = new { Error = ex.Message }
            };
        }

        // ── 2. Upload Klasörü Erişimi ──
        var uploadSub = _cfg.GetValue<string>("Upload:SubFolder") ?? "Data\\Raporlar";
        var uploadPath = Path.Combine(_env.WebRootPath ?? _env.ContentRootPath, uploadSub);

        try
        {
            var exists = Directory.Exists(uploadPath);
            if (!exists)
            {
                Directory.CreateDirectory(uploadPath);
                exists = true;
            }

            // Yazma testi
            var testFile = Path.Combine(uploadPath, ".health_check_test");
            await System.IO.File.WriteAllTextAsync(testFile, "ok", ct);
            System.IO.File.Delete(testFile);

            var fileCount = Directory.GetFiles(uploadPath, "*.*", SearchOption.AllDirectories).Length;

            checks["upload_folder"] = new HealthCheckResult
            {
                Status = "Healthy",
                Details = new
                {
                    Path = uploadPath,
                    Writable = true,
                    FileCount = fileCount
                }
            };
        }
        catch (Exception ex)
        {
            overallHealthy = false;
            checks["upload_folder"] = new HealthCheckResult
            {
                Status = "Unhealthy",
                Details = new { Path = uploadPath, Error = ex.Message }
            };
        }

        // ── 3. Disk Alanı ──
        try
        {
            var rootPath = _env.ContentRootPath;
            var driveRoot = Path.GetPathRoot(rootPath);
            if (!string.IsNullOrEmpty(driveRoot))
            {
                var driveInfo = new DriveInfo(driveRoot);
                var freeGb = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                var totalGb = driveInfo.TotalSize / (1024.0 * 1024.0 * 1024.0);
                var usedPercent = ((totalGb - freeGb) / totalGb) * 100;

                var diskHealthy = freeGb > 1.0; // 1 GB altı = Unhealthy
                if (!diskHealthy) overallHealthy = false;

                checks["disk"] = new HealthCheckResult
                {
                    Status = diskHealthy ? "Healthy" : "Warning",
                    Details = new
                    {
                        Drive = driveRoot,
                        FreeGB = Math.Round(freeGb, 2),
                        TotalGB = Math.Round(totalGb, 2),
                        UsedPercent = Math.Round(usedPercent, 1)
                    }
                };
            }
        }
        catch (Exception ex)
        {
            checks["disk"] = new HealthCheckResult
            {
                Status = "Unknown",
                Details = new { Error = ex.Message }
            };
        }

        var result = new
        {
            status = overallHealthy ? "Healthy" : "Unhealthy",
            timestamp = DateTime.UtcNow.ToString("o"),
            checks
        };

        return overallHealthy
            ? Ok(result)
            : StatusCode(503, result);
    }

    private sealed class HealthCheckResult
    {
        public string Status { get; set; } = "Unknown";
        public object? Details { get; set; }
    }
}
