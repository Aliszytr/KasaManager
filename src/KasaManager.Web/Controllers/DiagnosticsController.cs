using KasaManager.Infrastructure.Persistence;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Web.Controllers;

/// <summary>
/// DB'ye gerçekten yazıyor muyuz? Hangi instance/database'e bağlıyız?
/// - Ali'nin "UI çalışıyor ama DB boş" durumlarını netleştirmek için.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed class DiagnosticsController : Controller
{
    private readonly KasaManagerDbContext _db;
    private readonly IWebHostEnvironment _env;

    public DiagnosticsController(KasaManagerDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    [HttpGet]
    public async Task<IActionResult> Db(CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        var provider = _db.Database.ProviderName;

        var vm = new DbDiagnosticsViewModel
        {
            DataSource = conn.DataSource,
            Database = conn.Database,
            ProviderName = provider,
            ConnectionStringMasked = MaskConnectionString(_db.Database.GetConnectionString())
        };

        try
        {
            // "Bağlanabiliyor muyuz?" için hızlı ping
            vm.Notes.Add(_env.IsDevelopment() ? "Environment: Development" : "Environment: Production");
            vm.Notes.Add($"CanConnect: {await _db.Database.CanConnectAsync(ct)}");

            vm.SettingsCount = await _db.KasaGlobalDefaultsSettings.AsNoTracking().CountAsync(ct);
            vm.FormulaSetCount = await _db.FormulaSets.AsNoTracking().CountAsync(ct);
            vm.FormulaLineCount = await _db.FormulaLines.AsNoTracking().CountAsync(ct);
            vm.SnapshotCount = await _db.KasaRaporSnapshots.AsNoTracking().CountAsync(ct);

            vm.Notes.Add("Eğer SSMS'te boş görüyorsan: Büyük ihtimalle SSMS farklı SQL instance/database'e bağlı.");
            vm.Notes.Add("Yukarıdaki DataSource/Database değerleri, uygulamanın gerçekte yazdığı yeri gösterir.");
        }
        catch (Exception ex)
        {
            vm.Notes.Add($"Diagnostics error: {ex.GetType().Name}: {ex.Message}");
        }

        return View(vm);
    }

    private static string MaskConnectionString(string? connStr)
    {
        if (string.IsNullOrWhiteSpace(connStr))
            return string.Empty;

        // Basit maskeleme (Password=... / Pwd=...)
        var parts = connStr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.StartsWith("Password=", StringComparison.OrdinalIgnoreCase) || p.StartsWith("Pwd=", StringComparison.OrdinalIgnoreCase))
                parts[i] = p.Split('=')[0] + "=***";
        }
        return string.Join(";", parts);
    }
}
