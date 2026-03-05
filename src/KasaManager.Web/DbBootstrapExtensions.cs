using KasaManager.Domain.Identity;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Web;

/// <summary>
/// DB otomatik bootstrapping extension metodu.
/// Program.cs'ten ayrıştırılmıştır — migration, seed, admin user oluşturma.
/// </summary>
public static class DbBootstrapExtensions
{
    /// <summary>
    /// DB bootstrapping: Migration/EnsureCreated, GlobalDefaults seed, FormulaTemplate seed, Admin user seed.
    /// </summary>
    public static async Task UseDbBootstrapAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbBootstrap");
        var db = scope.ServiceProvider.GetRequiredService<KasaManagerDbContext>();

        try
        {
            var providerName = db.Database.ProviderName ?? string.Empty;

            if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                // ✅ SQLite (sadece explicit seçilirse)
                db.Database.EnsureCreated();
            }
            else
            {
                // ✅ SQL Server: HER ZAMAN Migrate() kullan.
                // Migration yoksa sıfırdan oluşturur, varsa pending olanları uygular.
                // NOT: EnsureCreated() fallback kaldırıldı çünkü sonraki migration'ları engelliyordu.
                // NOT: Migrate() idempotent çalışır — __EFMigrationsHistory tablosundan
                //      hangi migration'ların uygulandığını kontrol eder. Mevcut veriler korunur.

                // Migration öncesi durum bilgisi (kurumsal deploy takibi)
                try
                {
                    var applied = db.Database.GetAppliedMigrations().ToList();
                    var pending = db.Database.GetPendingMigrations().ToList();
                    logger.LogInformation(
                        "DB Migration durumu: {Applied} uygulanmış, {Pending} bekleyen migration",
                        applied.Count, pending.Count);

                    if (pending.Count > 0)
                    {
                        logger.LogInformation("Bekleyen migration'lar: {Migrations}",
                            string.Join(", ", pending));
                    }
                    else
                    {
                        logger.LogInformation("Tüm migration'lar güncel — veritabanı hazır.");
                    }
                }
                catch (Exception exInfo)
                {
                    // Migration bilgisi okunamadıysa (ilk kurulum olabilir), devam et
                    logger.LogDebug(exInfo, "Migration durum bilgisi okunamadı (ilk kurulum olabilir).");
                }

                db.Database.Migrate();
            }

            // 🔒 TEK SATIR SETTINGS SEED (Id=1 deterministik)
            var settings = db.KasaGlobalDefaultsSettings.FirstOrDefault(
                x => x.Id == KasaManager.Domain.Settings.KasaGlobalDefaultsSettings.SingletonId);
            if (settings is null)
            {
                db.KasaGlobalDefaultsSettings.Add(new KasaManager.Domain.Settings.KasaGlobalDefaultsSettings
                {
                    Id = KasaManager.Domain.Settings.KasaGlobalDefaultsSettings.SingletonId,
                    SelectedVeznedarlarJson = "[]",
                    LastUpdatedAt = DateTime.UtcNow,

                    // Güvenli default'lar
                    DefaultBozukPara = 0m,
                    DefaultNakitPara = 0m,
                    DefaultKasaEksikFazla = 0m,
                    DefaultGenelKasaDevredenSeed = 0m,
                    DefaultGenelKasaBaslangicTarihiSeed = null,
                    DefaultKaydenTahsilat = 0m
                });

                await db.SaveChangesAsync();
            }

            // Bootstrap başarılıysa warning değil info yazalım.
            logger.LogInformation("DB Bootstrap OK. Provider={Provider}, DataSource={DataSource}, Database={Database}",
                providerName,
                db.Database.GetDbConnection().DataSource,
                db.Database.GetDbConnection().Database);
            
            // R21: Varsayılan formül şablonlarını seed et
            var seeder = scope.ServiceProvider.GetRequiredService<KasaManager.Infrastructure.Services.IFormulaTemplateSeeder>();
            await seeder.SeedDefaultTemplatesAsync();
            logger.LogInformation("R21: Formül şablon seed'i tamamlandı.");

            // ✅ Faz H: Varsayılan admin kullanıcı seed
            if (!db.KasaUsers.Any())
            {
                var seedPassword = app.Configuration["SeedAdmin:Password"];
                if (string.IsNullOrWhiteSpace(seedPassword))
                {
                    logger.LogWarning("⚠️ SeedAdmin:Password yapılandırılmadı. Admin kullanıcı oluşturulamadı. appsettings.json veya environment variable ekleyin.");
                }
                else
                {
                    db.KasaUsers.Add(new KasaUser
                    {
                        Username = app.Configuration["SeedAdmin:Username"] ?? "admin",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(seedPassword),
                        DisplayName = app.Configuration["SeedAdmin:DisplayName"] ?? "Sistem Yöneticisi",
                        Role = "Admin"
                    });
                    await db.SaveChangesAsync();
                    logger.LogInformation("Faz H: Varsayılan admin kullanıcı oluşturuldu.");
                }
            }
        }
        catch (Exception ex)
        {
            // ❗ Uygulama patlamasın. Sorun Diagnostics/Db sayfasında da görünecek (CanConnect=false vs).
            logger.LogError(ex, "❌ DB Bootstrap FAILED. App will continue running.");
        }
    }
}
