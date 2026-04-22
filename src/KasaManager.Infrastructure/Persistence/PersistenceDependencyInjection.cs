using KasaManager.Application.Abstractions;
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace KasaManager.Infrastructure.Persistence;

public static class PersistenceDependencyInjection
{
    /// <summary>
    /// DB altyapısı (KİLİTLİ HEDEF: SQL Server default).
    ///
    /// Tek kaynak: ConnectionStrings:SqlConnection
    /// Çalışan projedeki pattern:
    ///   UseSqlServer(GetConnectionString("SqlConnection"), opt => opt.MigrationsAssembly(...))
    ///
    /// Opsiyonel (yalnızca özellikle istenirse): Database:Provider=Sqlite
    /// </summary>
    public static IServiceCollection AddSnapshotPersistence(this IServiceCollection services, IConfiguration configuration, IHostEnvironment env)
    {
        var provider = (configuration["Database:Provider"] ?? "SqlServer").Trim();

        services.AddDbContext<KasaManagerDbContext>(opt =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                // ✅ Sadece explicit olarak istenirse
                var relativePath = (configuration["Database:SqlitePath"] ?? "App_Data/KasaManager.db").Trim();
                var dbPath = Path.IsPathRooted(relativePath)
                    ? relativePath
                    : Path.Combine(env.ContentRootPath, relativePath);

                var dir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var sqliteConnStr = $"Data Source={dbPath}";
                opt.UseSqlite(sqliteConnStr, sqlite => sqlite.CommandTimeout(60));
                return;
            }

            // ✅ Default: SQL Server (tek kaynak)
            var connStr = configuration.GetConnectionString("SqlConnection")
                          ?? throw new InvalidOperationException(
                              "ConnectionStrings:SqlConnection tanımlı olmalı. (Kilitli hedef: tek kaynak, tek provider)");

            connStr = SqlServerConnectionStringResolver.ResolveOrFallback(connStr, configuration);

            opt.UseSqlServer(connStr, sql =>
            {
                sql.EnableRetryOnFailure(5);
                sql.CommandTimeout(60);

                // ✅ Migrations assembly'i explicit: Infrastructure
                sql.MigrationsAssembly(typeof(KasaManagerDbContext).Assembly.GetName().Name);
            });
        });

        // Snapshot servisleri aktif sistemden tamamen ayrıştırıldı (P4.4 Stateless Engine)
        // Eğer arşivi doğrudan DB'den okunacak bir duruma gelirse ArchiveService üzerinden yönetilecek.
        services.AddScoped<IKasaGlobalDefaultsService, KasaGlobalDefaultsService>();
        services.AddScoped<IFormulaSetStore, FormulaSetStore>();

        // R17: Field Chooser ve Calculated Kasa servisleri
        services.AddScoped<IFieldPreferenceService, KasaManager.Infrastructure.Services.FieldPreferenceService>();
        services.AddScoped<ICalculatedKasaSnapshotService, KasaManager.Infrastructure.Services.CalculatedKasaSnapshotService>(); // P4.4 İptali Geri Alındı - Okuma için hala controller'larda inject ediliyor.
        services.AddScoped<KasaManager.Application.Abstractions.IKasaRaporSnapshotService, KasaManager.Infrastructure.Persistence.KasaRaporSnapshotService>();

        // R19: InMemory caching - performans optimizasyonu
        services.AddMemoryCache();
        services.AddSingleton<KasaManager.Infrastructure.Caching.ICachingService, KasaManager.Infrastructure.Caching.InMemoryCachingService>();

        // R21: Varsayılan formül şablonları seeder
        services.AddScoped<KasaManager.Infrastructure.Services.IFormulaTemplateSeeder, KasaManager.Infrastructure.Services.FormulaTemplateSeeder>();

        // ═══ Çıktı Modülü (Output Module) ═══
        services.AddScoped<IReportDataBuilder, KasaManager.Infrastructure.Export.ReportDataBuilder>();
        services.AddScoped<IExportService, KasaManager.Infrastructure.Export.ExportService>();
        services.AddScoped<IDocumentTemplateService, KasaManager.Infrastructure.Export.DocumentTemplateService>();

        return services;
    }
}

/// <summary>
/// Local SQL instance belirsizliği yüzünden "DB'ye yazmıyor" gibi görünen sorunları bitirir.
/// - Eğer connStr'de Server=localhost/. gibi belirsiz bir değer varsa ve bağlantı açılmıyorsa,
///   Windows'taki yerel SQL instance'larını enumerate eder.
/// - Tek instance varsa otomatik seçer.
/// - Birden fazlaysa Database:PreferredInstance (opsiyonel) ile eşleştirmeye çalışır.
/// </summary>
internal static class SqlServerConnectionStringResolver
{
    public static string ResolveOrFallback(string rawConnStr, IConfiguration configuration)
    {
        var autoResolve = (configuration["Database:AutoInstanceResolve"] ?? "true").Trim();
        var enabled = autoResolve.Equals("true", StringComparison.OrdinalIgnoreCase);

        var preferred = (configuration["Database:PreferredInstance"] ?? string.Empty).Trim();

        // Zaten açıkça named instance verilmişse (Server=ALILAPTOP\ALISZ gibi), dokunma.
        if (!enabled || HasExplicitInstance(rawConnStr))
            return rawConnStr;

        // Yalnızca localhost / . / (local) gibi belirsiz değerlerde dene
        if (!IsAmbiguousLocalServer(rawConnStr))
            return rawConnStr;

        // 1) İlk olarak verilen connStr ile bağlanmayı dene
        if (CanOpen(rawConnStr))
            return rawConnStr;

        // 2) Local instance'ları bul
        var instances = EnumerateLocalInstances();

        if (instances.Count == 0)
            return rawConnStr; // enumerator yok / erişim yok

        // 3) PreferredInstance varsa onu yakala
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var match = instances.FirstOrDefault(x =>
                x.Equals(preferred, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith("\\" + preferred, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(match))
            {
                var candidate = ReplaceServer(rawConnStr, match);
                if (CanOpen(candidate))
                    return candidate;
            }
        }

        // 4) Tek instance varsa otomatik seç
        if (instances.Count == 1)
        {
            var candidate = ReplaceServer(rawConnStr, instances[0]);
            if (CanOpen(candidate))
                return candidate;
        }

        // 5) Birden fazla: sırayla dene, ilk çalışanı seç
        foreach (var inst in instances)
        {
            var candidate = ReplaceServer(rawConnStr, inst);
            if (CanOpen(candidate))
                return candidate;
        }

        return rawConnStr;
    }

    private static bool CanOpen(string connStr)
    {
        try
        {
            using var c = new SqlConnection(connStr);
            c.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasExplicitInstance(string connStr)
    {
        // Server/Data Source içinde backslash varsa (MACHINE\INSTANCE) explicit say.
        var server = GetServerValue(connStr);
        return server.Contains("\\", StringComparison.Ordinal);
    }

    private static bool IsAmbiguousLocalServer(string connStr)
    {
        var server = GetServerValue(connStr).Trim();

        return server.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || server.Equals(".", StringComparison.OrdinalIgnoreCase)
            || server.Equals("(local)", StringComparison.OrdinalIgnoreCase)
            || server.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetServerValue(string connStr)
    {
        var parts = connStr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            if (p.StartsWith("Server=", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                var kv = p.Split('=', 2);
                if (kv.Length == 2)
                    return kv[1].Trim();
            }
        }
        return string.Empty;
    }

    private static string ReplaceServer(string connStr, string newServer)
    {
        var parts = connStr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        for (var i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (p.StartsWith("Server=", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                var kv = p.Split('=', 2);
                if (kv.Length == 2)
                {
                    parts[i] = kv[0] + "=" + newServer;
                }
            }
        }
        return string.Join(';', parts);
    }

    private static List<string> EnumerateLocalInstances()
    {
        // Bu resolver Windows registry'ye dayanır.
        // Uygulama cross-platform derlense bile burada patlamasın / uyarı üretmesin diye Windows guard.
        if (!OperatingSystem.IsWindows())
            return new List<string>();

// NOT: Microsoft.Data.SqlClient içinde SqlDataSourceEnumerator yok.
        // Bu yüzden Windows registry üzerinden local instance isimlerini okuyoruz.
        // Kaynak: HKLM\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL
        // (bazı sistemlerde WOW6432Node altında da olabilir)
        var machine = Environment.MachineName;
        var instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void ReadInstancesFromKey(string keyPath)
        {
            try
            {
                #pragma warning disable CA1416
                using var key = Registry.LocalMachine.OpenSubKey(keyPath);
#pragma warning restore CA1416
                if (key is null) return;

                #pragma warning disable CA1416
                foreach (var name in key.GetValueNames())
#pragma warning restore CA1416
                {
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    // name = instanceName (MSSQLSERVER, SQLEXPRESS, ALISZ, ...)
                    // Default instance (MSSQLSERVER) için server = MACHINE
                    if (name.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
                    {
                        instances.Add(machine);
                        instances.Add("localhost");
                        instances.Add(".");
                    }
                    else
                    {
                        instances.Add($"{machine}\\{name}");
                        instances.Add($".\\{name}");
                        instances.Add($"localhost\\{name}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SqlServerInstanceResolve] Registry okuma hatası: {ex.Message}");
            }
        }

        ReadInstancesFromKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
        ReadInstancesFromKey(@"SOFTWARE\WOW6432Node\Microsoft\Microsoft SQL Server\Instance Names\SQL");

        return instances.ToList();
    }
}
