using KasaManager.Domain.FinancialExceptions;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KasaManager.Tests.Infrastructure;

/// <summary>
/// CreateFromDevredilmisAsync idempotency guard testleri.
/// InMemory DbContext ile service-level doğrulama.
/// </summary>
public sealed class FinansalIstisnaIdempotencyTests : IDisposable
{
    private readonly KasaManagerDbContext _db;
    private readonly FinansalIstisnaService _svc;

    public FinansalIstisnaIdempotencyTests()
    {
        var opts = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase($"IdempotencyTest_{Guid.NewGuid()}")
            .Options;
        _db = new KasaManagerDbContext(opts);
        _svc = new FinansalIstisnaService(_db);
    }

    [Fact]
    public async Task CreateFromDevredilmis_SameParentSameDay_ThrowsOnSecondCall()
    {
        var parent = SeedDevredilmisParent();
        var bugun = DateOnly.FromDateTime(DateTime.Today);

        // İlk çağrı başarılı olmalı
        var child = await _svc.CreateFromDevredilmisAsync(parent.Id, bugun, "user1");
        Assert.NotNull(child);
        Assert.Equal(parent.Id, child.ParentIstisnaId);

        // İkinci çağrı (duplicate) reddedilmeli
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.CreateFromDevredilmisAsync(parent.Id, bugun, "user1"));

        Assert.Contains("zaten açılmış", ex.Message);
    }

    [Fact]
    public async Task CreateFromDevredilmis_SameParentDifferentDay_Succeeds()
    {
        var parent = SeedDevredilmisParent();
        var bugun = DateOnly.FromDateTime(DateTime.Today);
        var yarin = bugun.AddDays(1);

        await _svc.CreateFromDevredilmisAsync(parent.Id, bugun, "user1");
        var child2 = await _svc.CreateFromDevredilmisAsync(parent.Id, yarin, "user1");

        Assert.NotNull(child2);
        Assert.Equal(yarin, child2.IslemTarihi);
    }

    [Fact]
    public async Task CreateFromDevredilmis_DifferentParentSameDay_Succeeds()
    {
        var parent1 = SeedDevredilmisParent();
        var parent2 = SeedDevredilmisParent();
        var bugun = DateOnly.FromDateTime(DateTime.Today);

        var child1 = await _svc.CreateFromDevredilmisAsync(parent1.Id, bugun, "user1");
        var child2 = await _svc.CreateFromDevredilmisAsync(parent2.Id, bugun, "user1");

        Assert.NotNull(child1);
        Assert.NotNull(child2);
        Assert.NotEqual(child1.Id, child2.Id);
    }

    [Fact]
    public async Task CreateFromDevredilmis_CancelledChildAllowsNewCreate()
    {
        var parent = SeedDevredilmisParent();
        var bugun = DateOnly.FromDateTime(DateTime.Today);

        // İlk child oluştur ve iptal et
        var child1 = await _svc.CreateFromDevredilmisAsync(parent.Id, bugun, "user1");
        await _svc.SetDurumAsync(child1.Id, IstisnaDurumu.Iptal, "user1");

        // İptal edildikten sonra yeni child oluşturulabilmeli
        var child2 = await _svc.CreateFromDevredilmisAsync(parent.Id, bugun, "user1");
        Assert.NotNull(child2);
        Assert.NotEqual(child1.Id, child2.Id);
    }

    // ═══════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════

    private FinansalIstisna SeedDevredilmisParent()
    {
        var parent = new FinansalIstisna
        {
            Id = Guid.NewGuid(),
            IslemTarihi = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
            Tur = IstisnaTuru.BasarisizVirman,
            Kategori = IstisnaKategorisi.BankaTransferHatasi,
            HesapTuru = KasaManager.Domain.Reports.HesapKontrol.BankaHesapTuru.Tahsilat,
            BeklenenTutar = 5000m,
            GerceklesenTutar = 0m,
            SistemeGirilenTutar = 0m,
            EtkiYonu = KasaEtkiYonu.Artiran,
            KararDurumu = KararDurumu.Onaylandi,
            Durum = IstisnaDurumu.ErtesiGuneDevredildi,
            OlusturanKullanici = "seed"
        };
        _db.FinansalIstisnalar.Add(parent);
        _db.SaveChanges();
        return parent;
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}
