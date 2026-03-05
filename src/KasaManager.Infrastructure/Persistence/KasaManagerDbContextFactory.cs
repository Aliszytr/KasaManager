using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace KasaManager.Infrastructure.Persistence;

/// <summary>
/// EF Core design-time factory.
///
/// Amaç: "dotnet ef migrations ..." komutlarının her ortamda (VS / CLI) stabil çalışması.
///
/// Provider seçimi:
/// - Varsayılan: SQLite (portable, tek dosya)
/// - Alternatif: SQL Server
///
/// Not: Startup project (KasaManager.Web) içinden çalıştırılması önerilir:
///   dotnet ef migrations add InitialCreate -p ../KasaManager.Infrastructure -s .
/// </summary>
public sealed class KasaManagerDbContextFactory : IDesignTimeDbContextFactory<KasaManagerDbContext>
{
    public KasaManagerDbContext CreateDbContext(string[] args)
    {
        // 1) Env var ile override edilebilir
        //    setx KASAMANAGER_DB_PROVIDER "Sqlite" | "SqlServer"
        //    setx KASAMANAGER_SQLSERVER "Server=...;Database=...;Trusted_Connection=True;TrustServerCertificate=True"
        //    setx KASAMANAGER_SQLITE_PATH "C:\\...\\KasaManager.db"
        var envProvider = Environment.GetEnvironmentVariable("KASAMANAGER_DB_PROVIDER");
        var envSqlServer = Environment.GetEnvironmentVariable("KASAMANAGER_SQLSERVER");
        var envSqlitePath = Environment.GetEnvironmentVariable("KASAMANAGER_SQLITE_PATH");

        // 2) Yoksa startup project'in bulunduğu klasördeki appsettings.json okunur
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var provider = (envProvider
                       ?? config["Database:Provider"]
                       ?? "Sqlite").Trim();

        var optionsBuilder = new DbContextOptionsBuilder<KasaManagerDbContext>();

        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            var connStr = envSqlServer
                         ?? config.GetConnectionString("SqlConnection")
                         ?? config.GetConnectionString("KasaManager")
                         ?? config.GetConnectionString("DefaultConnection")
                         ?? throw new InvalidOperationException(
                             "SQL Server bağlantı cümlesi bulunamadı. appsettings.json içinde ConnectionStrings:SqlConnection (veya KasaManager/DefaultConnection) olmalı ya da KASAMANAGER_SQLSERVER env var set edilmeli.");


            optionsBuilder.UseSqlServer(connStr);
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
}
