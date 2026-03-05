#nullable enable
using KasaManager.Domain.Legacy;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Infrastructure.Legacy;

/// <summary>
/// Eski KasaRaporuDB veritabanı için read-only DbContext.
/// Migration üretilmez, sadece okuma yapılır.
/// </summary>
public sealed class LegacyKasaDbContext : DbContext
{
    public LegacyKasaDbContext(DbContextOptions<LegacyKasaDbContext> options)
        : base(options)
    {
        // Read-only: Change Tracker kapalı
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    public DbSet<LegacySabahKasa> SabahKasaNesnesis => Set<LegacySabahKasa>();
    public DbSet<LegacyAksamKasa> AksamKasaNesnesis => Set<LegacyAksamKasa>();
    public DbSet<LegacyGenelKasaRapor> GenelKasaRaporNesnesis => Set<LegacyGenelKasaRapor>();

    /// <summary>
    /// SaveChanges engellenmiştir — bu veritabanı read-only.
    /// </summary>
    public override int SaveChanges()
        => throw new InvalidOperationException("Legacy veritabanı read-only'dir. Yazma işlemleri desteklenmez.");

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
        => throw new InvalidOperationException("Legacy veritabanı read-only'dir. Yazma işlemleri desteklenmez.");
}
