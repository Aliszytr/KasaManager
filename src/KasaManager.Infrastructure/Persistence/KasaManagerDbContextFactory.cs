using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace KasaManager.Infrastructure.Persistence;

/// <summary>
/// EF Core design-time factory.
///
/// Amaç: "dotnet ef migrations ..." komutlarının her ortamda (VS / CLI) stabil çalışması VE
/// runtime (Program.cs/WebApplication.CreateBuilder → AddSnapshotPersistence) ile AYNI
/// authority modelini kullanması — bkz. KasaManager Revision 3 EF Production Migration Authority
/// Fix. Bu iki authority birbirinden sapabilirse ("dotnet ef database update" farklı bir DB'ye
/// bağlanabilirse) Production migration invocation'ı non-deterministic ve güvensiz olur.
///
/// Environment resolution (runtime ile birebir aynı sıra — WebApplication.CreateBuilder'ın
/// altındaki modern .NET generic host, DOTNET_ENVIRONMENT'ı ASPNETCORE_ENVIRONMENT'a göre
/// ÖNCELİKLİ okur): DOTNET_ENVIRONMENT, yoksa ASPNETCORE_ENVIRONMENT, ikisi de yoksa "Development"
/// (yalnızca sıradan yerel "dotnet ef" kullanımı için — mevcut repo konvansiyonu). Bu varsayılan
/// asla EXPLICIT olarak set edilmiş bir Production (veya başka) ortamın yerini alamaz; yalnızca
/// hiçbir ortam değişkeni set edilmemişken devreye girer.
///
/// Configuration precedence (runtime ile birebir aynı): appsettings.json → appsettings.
/// {Environment}.json → ortam değişkenleri → (yalnızca SQL Server dalında) mevcut/legacy açık
/// override mekanizmaları (KASAMANAGER_DB_PROVIDER/KASAMANAGER_SQLSERVER — bilinçli, açık operatör
/// override'ı; sessiz fallback değildir).
///
/// SQL Server bağlantı cümlesi kaynağı runtime ile birebir aynıdır: yalnızca
/// ConnectionStrings:SqlConnection (bkz. PersistenceDependencyInjection.AddSnapshotPersistence).
/// Aynı SqlServerConnectionStringResolver.ResolveOrFallback burada da kullanılır — ikinci, rakip
/// bir çözümleme algoritması İCAT EDİLMEZ.
///
/// FAIL-CLOSED: SqlConnection hiçbir kaynaktan (appsettings.{Environment}.json, appsettings.json,
/// ortam değişkenleri, açık override) çözülemezse exception fırlatılır — hiçbir zaman sessizce
/// Development config'e, LocalDB'ye veya başka bir varsayılana düşülmez. Hata mesajı yalnızca
/// environment adını ve eksik konfigürasyon anahtarını içerir; connection string/secret asla
/// mesaja yazılmaz.
///
/// Not: Startup project (KasaManager.Web) içinden çalıştırılması gerekir (appsettings dosyalarının
/// bulunduğu klasör = current directory):
///   dotnet ef migrations add InitialCreate -p ../KasaManager.Infrastructure -s .
/// </summary>
public sealed class KasaManagerDbContextFactory : IDesignTimeDbContextFactory<KasaManagerDbContext>
{
    public KasaManagerDbContext CreateDbContext(string[] args)
    {
        var environmentName = ResolveEffectiveEnvironment();

        // Yalnızca açık, bilinçli operatör override'ları (setx ile kalıcı set edilir) — sessiz
        // fallback değildir; env var set edilmemişse devreye girmez.
        //    setx KASAMANAGER_DB_PROVIDER "Sqlite" | "SqlServer"
        //    setx KASAMANAGER_SQLSERVER "Server=...;Database=...;Trusted_Connection=True;TrustServerCertificate=True"
        //    setx KASAMANAGER_SQLITE_PATH "C:\\...\\KasaManager.db"
        var envProvider = Environment.GetEnvironmentVariable("KASAMANAGER_DB_PROVIDER");
        var envSqlServer = Environment.GetEnvironmentVariable("KASAMANAGER_SQLSERVER");
        var envSqlitePath = Environment.GetEnvironmentVariable("KASAMANAGER_SQLITE_PATH");

        // Runtime (WebApplication.CreateBuilder) ile birebir aynı precedence: base → yalnızca
        // ÇÖZÜLMÜŞ ortama özgü dosya (asla sabit "Development") → ortam değişkenleri.
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Runtime varsayılanıyla aynı (PersistenceDependencyInjection.AddSnapshotPersistence):
        // Provider belirtilmemişse SqlServer varsayılır — sessizce Sqlite'a düşüp konfigürasyon
        // eksikliğini maskelemez.
        var provider = (envProvider
                       ?? config["Database:Provider"]
                       ?? "SqlServer").Trim();

        var optionsBuilder = new DbContextOptionsBuilder<KasaManagerDbContext>();

        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            // Runtime ile birebir aynı tek kaynak: yalnızca ConnectionStrings:SqlConnection.
            // (Eski KasaManager/DefaultConnection fallback anahtarları kaldırıldı — runtime'da
            // hiç var olmayan, yalnızca design-time'a özgü rakip bir çözümleme yoluydu.)
            var connStr = envSqlServer ?? config.GetConnectionString("SqlConnection");

            if (string.IsNullOrWhiteSpace(connStr))
            {
                throw new InvalidOperationException(
                    $"[EF-DESIGNTIME-FAIL-CLOSED] Environment='{environmentName}': " +
                    "ConnectionStrings:SqlConnection çözülemedi. Taranan kaynaklar: appsettings.json, " +
                    $"appsettings.{environmentName}.json, ortam değişkenleri, KASAMANAGER_SQLSERVER. " +
                    "Migration/design-time komutu fail-closed olarak durduruldu — Development " +
                    "config'e, LocalDB'ye veya başka bir varsayılana sessizce düşülmedi.");
            }

            // Runtime ile AYNI resolver — ikinci, rakip bir SQL Server çözümleme algoritması yok.
            connStr = SqlServerConnectionStringResolver.ResolveOrFallback(connStr, config);

            optionsBuilder.UseSqlServer(connStr, sql =>
                sql.MigrationsAssembly(typeof(KasaManagerDbContext).Assembly.GetName().Name));
        }
        else
        {
            var relativePath = (envSqlitePath
                                ?? config["Database:SqlitePath"]
                                ?? "App_Data/KasaManager.db").Trim();

            // Design-time'da ContentRoot yok; base path = current directory.
            var dbPath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(Directory.GetCurrentDirectory(), relativePath);

            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        return new KasaManagerDbContext(optionsBuilder.Options);
    }

    /// <summary>
    /// Runtime (modern .NET generic host altında WebApplication.CreateBuilder) ile aynı sırayla:
    /// DOTNET_ENVIRONMENT ÖNCE değerlendirilir, sonra ASPNETCORE_ENVIRONMENT. İkisi de set
    /// edilmemişse "Development" — yalnızca sıradan yerel "dotnet ef" kullanımı için korunan
    /// mevcut repo konvansiyonu; explicit set edilmiş bir ortamın (ör. Production) yerini ASLA
    /// almaz. Sıra ters olsaydı (ASPNETCORE_ENVIRONMENT önce), DOTNET_ENVIRONMENT=Development +
    /// ASPNETCORE_ENVIRONMENT=Production gibi çelişen bir ambient state'te factory Production
    /// seçerken runtime sessizce Development seçebilirdi — authority ayrışması.
    /// </summary>
    private static string ResolveEffectiveEnvironment()
    {
        var fromDotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (!string.IsNullOrWhiteSpace(fromDotnet))
            return fromDotnet.Trim();

        var fromAspNetCore = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (!string.IsNullOrWhiteSpace(fromAspNetCore))
            return fromAspNetCore.Trim();

        return "Development";
    }
}
