#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Application.Services;
using KasaManager.Domain.Projection;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Application.Observability;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace KasaManager.Tests.Application;

/// <summary>
/// P2: EksikFazlaProjectionEngine unit tests.
/// Direct engine tests — no BuildAsync dependency.
/// </summary>
public sealed class EksikFazlaProjectionEngineTests
{
    private readonly ITestOutputHelper _out;
    private readonly Mock<IBankaHesapKontrolService> _hkMock = new();
    private readonly Dictionary<DateOnly, ProjectionDayInput> _inputs = new();

    public EksikFazlaProjectionEngineTests(ITestOutputHelper output) => _out = output;

    private Task<ProjectionDayInput?> DefaultInputProvider(DateOnly d, CancellationToken ct) => 
        Task.FromResult(_inputs.TryGetValue(d, out var v) ? v : null);

    private EksikFazlaProjectionEngine CreateSut()
    {
        // Default: HesapKontrol returns empty
        _hkMock
            .Setup(h => h.GetHistoryAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                It.IsAny<BankaHesapTuru?>(), It.IsAny<KayitDurumu?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HesapKontrolKaydi>());

        return new EksikFazlaProjectionEngine(
            _hkMock.Object,
            Mock.Of<ILogger<EksikFazlaProjectionEngine>>(),
            Mock.Of<IAlertService>());
    }

    // ═══════════════════════════════════════
    // T1: 3+ gün recursive zincir
    // ═══════════════════════════════════════

    [Fact]
    public async Task T1_ThreeDayChain_CarryForwardCorrect()
    {
        // Arrange: 3 days of snapshots with different values
        var day3 = new DateOnly(2026, 4, 3); // today
        var day2 = new DateOnly(2026, 4, 2);
        var day1 = new DateOnly(2026, 4, 1);

        // GuneAit formül: BankaGiren - ((OnlineTahsilat - OnlineReddiyat) + (Tahsilat - BankayaYatirilacakNakit))
        // Snapshot-based: BankaGiren = 0 (snapshot'ta yok)
        // Sonuç: guneTahsilat = 0 - ((OnlineTahsilat - 0) + (Tahsilat - BankayaYatirilacakNakit))
        // = -(OnlineTahsilat + Tahsilat - BankayaYatirilacakNakit)
        // BankayaYatirilacakNakit = Max(0, Max(0, Tahsilat - Max(0, Reddiyat - 0)) - VergiKasa)
        //                         = Max(0, Max(0, Tahsilat - Reddiyat) - VergiKasa)

        SetupSnapshot(day1, tahsilat: 1000, reddiyat: 200, harc: 150, onlineTahsilat: 100, vergiKasa: 0);
        SetupSnapshot(day2, tahsilat: 2000, reddiyat: 300, harc: 250, onlineTahsilat: 200, vergiKasa: 0);
        SetupSnapshot(day3, tahsilat: 3000, reddiyat: 400, harc: 350, onlineTahsilat: 300, vergiKasa: 0);

        var sut = CreateSut();
        var req = new ProjectionRequest(day3, "C:\\fake", 14, InputProvider: DefaultInputProvider);

        // Act
        var result = await sut.ProjectAsync(req);

        // Assert
        _out.WriteLine($"Ok={result.Ok}, Chain={result.Chain.Count} days");
        foreach (var node in result.Chain)
            _out.WriteLine($"  {node.Date}: Depth={node.Depth}, Source={node.Source}, " +
                $"GuneT={node.RawGuneTahsilat:N2}, OncekiT={node.OncekiTahsilat:N2}, DundenT={node.DundenTahsilat:N2}");

        _out.WriteLine($"Final: OncekiT={result.OncekiTahsilat:N2}, DundenT={result.DundenTahsilat:N2}, " +
            $"GuneT={result.GuneTahsilat:N2}");

        Assert.True(result.Ok);
        // 3 data nodes (day3, day2, day1) + 1 NoData base case (day0) = 4
        Assert.Equal(4, result.Chain.Count);
        Assert.True(result.Chain.Count >= 3, $"Expected at least 3 chain nodes, got {result.Chain.Count}");

        // Verify chain direction: [0]=today, [1]=yesterday, [2]=day before
        Assert.Equal(day3, result.Chain[0].Date);
        Assert.Equal(day2, result.Chain[1].Date);
        Assert.Equal(day1, result.Chain[2].Date);

        // Carry-forward: day1's DundenT should be 0 (base case)
        // day2's DundenT should be day1's GuneT
        // day3's DundenT should be day2's GuneT
        Assert.Equal(0m, result.Chain[2].DundenTahsilat); // day1: base case dünden=0
    }

    // ═══════════════════════════════════════
    // T2: Eksik previous day → zeros
    // ═══════════════════════════════════════

    [Fact]
    public async Task T2_NoPreviousDaySnapshot_ReturnsZeros()
    {
        var today = new DateOnly(2026, 4, 5);
        SetupSnapshot(today, tahsilat: 5000, reddiyat: 500, harc: 100, onlineTahsilat: 200, vergiKasa: 0);
        // No yesterday snapshot → chain breaks

        var sut = CreateSut();
        var result = await sut.ProjectAsync(new ProjectionRequest(today, "C:\\fake", InputProvider: DefaultInputProvider));

        _out.WriteLine($"Ok={result.Ok}, Chain={result.Chain.Count}");
        Assert.True(result.Ok);
        Assert.Equal(0m, result.OncekiTahsilat);
        Assert.Equal(0m, result.DundenTahsilat);
        Assert.Equal(0m, result.OncekiHarc);
        Assert.Equal(0m, result.DundenHarc);
    }

    // ═══════════════════════════════════════
    // T4: Snapshot var/yok — chain break
    // ═══════════════════════════════════════

    [Fact]
    public async Task T4_MidChainBreak_StopsAtGap()
    {
        var day3 = new DateOnly(2026, 4, 3);
        var day2 = new DateOnly(2026, 4, 2);
        // day1 = no snapshot → gap

        SetupSnapshot(day3, tahsilat: 3000, reddiyat: 300, harc: 200, onlineTahsilat: 100, vergiKasa: 0);
        SetupSnapshot(day2, tahsilat: 2000, reddiyat: 200, harc: 150, onlineTahsilat: 50, vergiKasa: 0);

        var sut = CreateSut();
        var result = await sut.ProjectAsync(new ProjectionRequest(day3, "C:\\fake", InputProvider: DefaultInputProvider));

        _out.WriteLine($"Chain: {result.Chain.Count} nodes");
        foreach (var n in result.Chain)
            _out.WriteLine($"  {n.Date}: HasData={n.HasData}, Source={n.Source}");

        Assert.True(result.Ok);
        // day3 + day2 + day1(NoData) = 3 nodes
        Assert.True(result.Chain.Count >= 2);

        // day2 should have dünden=0 (because day1 is base case)
        var day2Node = result.Chain.First(n => n.Date == day2);
        Assert.Equal(0m, day2Node.DundenTahsilat);
    }

    // ═══════════════════════════════════════
    // T7: HesapKontrol fail → graceful fallback
    // ═══════════════════════════════════════

    [Fact]
    public async Task T7_HesapKontrolFails_ReturnsRawChain()
    {
        var today = new DateOnly(2026, 4, 5);
        SetupSnapshot(today, tahsilat: 1000, reddiyat: 100, harc: 50, onlineTahsilat: 50, vergiKasa: 0);

        // HesapKontrol throws
        _hkMock
            .Setup(h => h.GetHistoryAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                It.IsAny<BankaHesapTuru?>(), It.IsAny<KayitDurumu?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB connection failed"));

        var sut = CreateSut();
        var result = await sut.ProjectAsync(new ProjectionRequest(today, "C:\\fake", InputProvider: DefaultInputProvider));

        Assert.True(result.Ok);
        Assert.False(result.HesapKontrolApplied);
    }

    // ═══════════════════════════════════════
    // T9: Zero-edge
    // ═══════════════════════════════════════

    [Fact]
    public async Task T9_AllZeros_ReturnsAllZeros()
    {
        var today = new DateOnly(2026, 4, 1);
        SetupSnapshot(today, tahsilat: 0, reddiyat: 0, harc: 0, onlineTahsilat: 0, vergiKasa: 0);

        var sut = CreateSut();
        var result = await sut.ProjectAsync(new ProjectionRequest(today, "C:\\fake", InputProvider: DefaultInputProvider));

        Assert.True(result.Ok);
        Assert.Equal(0m, result.OncekiTahsilat);
        Assert.Equal(0m, result.DundenTahsilat);
        Assert.Equal(0m, result.GuneTahsilat);
        Assert.Equal(0m, result.OncekiHarc);
        Assert.Equal(0m, result.DundenHarc);
        Assert.Equal(0m, result.GuneHarc);
    }

    // ═══════════════════════════════════════
    // T10: Tek gün (depth=0)
    // ═══════════════════════════════════════

    [Fact]
    public async Task T10_SingleDay_GuneCalculated_DundenZero()
    {
        var today = new DateOnly(2026, 4, 1);
        // Only today has snapshot, no previous day
        SetupSnapshot(today, tahsilat: 5000, reddiyat: 500, harc: 200, onlineTahsilat: 300, vergiKasa: 100);

        var sut = CreateSut();
        var result = await sut.ProjectAsync(new ProjectionRequest(today, "C:\\fake", InputProvider: DefaultInputProvider));

        Assert.True(result.Ok);
        Assert.Equal(0m, result.DundenTahsilat);
        Assert.Equal(0m, result.OncekiTahsilat);
        Assert.Equal(0m, result.DundenHarc);
        Assert.Equal(0m, result.OncekiHarc);

        // GuneT should be computed (may be non-zero)
        _out.WriteLine($"GuneT={result.GuneTahsilat:N2}, GuneH={result.GuneHarc:N2}");

        // Chain should have at least 1 data node + 1 NoData (base case)
        Assert.True(result.Chain.Count >= 1);
        Assert.True(result.Chain[0].HasData);
    }

    // ═══════════════════════════════════════
    // T3: MaxDepth 14 days — depth guard
    // ═══════════════════════════════════════

    [Fact]
    public async Task T3_MaxDepth_DoesNotExceed14Plus1()
    {
        var today = new DateOnly(2026, 4, 20);

        // Setup 20 days of snapshots
        for (int i = 0; i <= 20; i++)
        {
            var d = today.AddDays(-i);
            SetupSnapshot(d, tahsilat: 1000 + i * 100, reddiyat: 50, harc: 30, onlineTahsilat: 20, vergiKasa: 0);
        }

        var sut = CreateSut();
        var result = await sut.ProjectAsync(new ProjectionRequest(today, "C:\\fake", MaxLookbackDays: 14, InputProvider: DefaultInputProvider));

        Assert.True(result.Ok);
        // Chain should have max 14+1 entries (0..14 depth inclusive)
        _out.WriteLine($"Chain length: {result.Chain.Count}");
        Assert.True(result.Chain.Count <= 16, $"Chain too long: {result.Chain.Count}");
    }

    // ═══════════════════════════════════════
    // T-RESULT: Result structure validation
    // ═══════════════════════════════════════

    [Fact]
    public async Task ResultContainsAuditTrail()
    {
        var today = new DateOnly(2026, 4, 1);
        SetupSnapshot(today, tahsilat: 1000, reddiyat: 100, harc: 50, onlineTahsilat: 50, vergiKasa: 0);

        var sut = CreateSut();
        var result = await sut.ProjectAsync(new ProjectionRequest(today, "C:\\fake", InputProvider: DefaultInputProvider));

        Assert.True(result.Ok);
        Assert.NotEmpty(result.Chain);

        var firstNode = result.Chain[0];
        Assert.Equal(today, firstNode.Date);
        Assert.Equal(0, firstNode.Depth);
        Assert.Equal(ProjectionDaySource.CalculatedSnapshot, firstNode.Source);
        Assert.True(firstNode.HasData);
    }

    // ═══════════════════════════════════════
    // T-NODATA: Completely empty — no snapshots
    // ═══════════════════════════════════════

    [Fact]
    public async Task NoSnapshotAtAll_ReturnsOkWithZeros()
    {
        var today = new DateOnly(2026, 4, 1);
        // No snapshots at all

        var sut = CreateSut();
        var result = await sut.ProjectAsync(new ProjectionRequest(today, "C:\\fake", InputProvider: DefaultInputProvider));

        Assert.True(result.Ok);
        Assert.Equal(0m, result.GuneTahsilat);
        Assert.Equal(0m, result.GuneHarc);
        // Chain should have 1 NoData node
        Assert.Single(result.Chain);
        Assert.False(result.Chain[0].HasData);
        Assert.Equal(ProjectionDaySource.NoData, result.Chain[0].Source);
    }

    // ─── Helpers ───

    private void SetupSnapshot(DateOnly date, decimal tahsilat, decimal reddiyat,
        decimal harc, decimal onlineTahsilat, decimal vergiKasa)
    {
        var onlineReddiyat = 0m;
        var normalReddiyat = Math.Max(0m, reddiyat - onlineReddiyat);
        var baseMasraf = Math.Max(0m, tahsilat - normalReddiyat);
        var bankayaYatirilacakNakit = Math.Max(0m, baseMasraf - vergiKasa);

        _inputs[date] = new ProjectionDayInput(
            Date: date,
            BankaGirenTahsilat: 0m,
            BankaGirenHarc: 0m,
            ToplamTahsilat: tahsilat,
            OnlineTahsilat: onlineTahsilat,
            ToplamHarc: harc,
            OnlineReddiyat: onlineReddiyat,
            OnlineHarc: 0m,
            BankayaYatirilacakNakit: bankayaYatirilacakNakit,
            NormalHarc: harc,
            Source: ProjectionDaySource.CalculatedSnapshot
        );
    }

    // ═══════════════════════════════════════
    // P2.1: InputProvider tests
    // ═══════════════════════════════════════

    /// <summary>
    /// P2.1-T1: InputProvider ile tam veri sağlandığında
    /// GuneT = BankaGiren - ((OnlineTahsilat - OnlineReddiyat) + (Tahsilat - BankayaYatirilacakNakit))
    /// hesabı doğru çalışır.
    /// </summary>
    [Fact]
    public async Task P21_InputProvider_FullData_ParityFormula()
    {
        var today = new DateOnly(2026, 4, 5);

        // Deterministik değerler:
        // Tahsilat=1000, Reddiyat=200, OnlineTahsilat=100, OnlineReddiyat=50
        // VergiKasa=0 → BankayaYatirilacakNakit = Max(0, Max(0, 1000 - Max(0, 200-50)) - 0) = Max(0, 850) = 850
        // BankaGiren=500
        // GuneTahsilat = 500 - ((100 - 50) + (1000 - 850)) = 500 - (50 + 150) = 500 - 200 = 300
        DayInputProvider provider = (DateOnly d, CancellationToken ct) =>
        {
            if (d != today) return Task.FromResult<ProjectionDayInput?>(null);

            return Task.FromResult<ProjectionDayInput?>(new ProjectionDayInput(
                Date: d,
                BankaGirenTahsilat: 500m,
                BankaGirenHarc: 100m,
                ToplamTahsilat: 1000m,
                OnlineTahsilat: 100m,
                ToplamHarc: 200m,
                OnlineReddiyat: 50m,
                OnlineHarc: 30m,
                BankayaYatirilacakNakit: 850m,
                NormalHarc: 200m,
                Source: ProjectionDaySource.ExcelAndSnapshot));
        };

        var sut = CreateSut();
        var req = new ProjectionRequest(today, "C:\\fake", InputProvider: provider);
        var result = await sut.ProjectAsync(req);

        _out.WriteLine($"GuneT={result.GuneTahsilat:N2}, GuneH={result.GuneHarc:N2}");

        Assert.True(result.Ok);
        // GuneTahsilat = 500 - ((100 - 50) + (1000 - 850)) = 300
        Assert.Equal(300m, result.GuneTahsilat);
        // GuneHarc = 100 - (30 + 200) = -130
        Assert.Equal(-130m, result.GuneHarc);
    }

    /// <summary>
    /// P2.1-T2: InputProvider ile 3 günlük chain — carry-forward doğrulaması.
    /// </summary>
    [Fact]
    public async Task P21_InputProvider_ThreeDay_CarryForward()
    {
        var day3 = new DateOnly(2026, 4, 3);
        var day2 = new DateOnly(2026, 4, 2);
        var day1 = new DateOnly(2026, 4, 1);

        // Her gün aynı değerler → GuneT = 300, GuneH = -130 (önceki testten)
        DayInputProvider provider = (DateOnly d, CancellationToken ct) =>
        {
            if (d < day1) return Task.FromResult<ProjectionDayInput?>(null);

            return Task.FromResult<ProjectionDayInput?>(new ProjectionDayInput(
                Date: d,
                BankaGirenTahsilat: 500m,
                BankaGirenHarc: 100m,
                ToplamTahsilat: 1000m,
                OnlineTahsilat: 100m,
                ToplamHarc: 200m,
                OnlineReddiyat: 50m,
                OnlineHarc: 30m,
                BankayaYatirilacakNakit: 850m,
                NormalHarc: 200m,
                Source: ProjectionDaySource.ExcelAndSnapshot));
        };

        var sut = CreateSut();
        var req = new ProjectionRequest(day3, "C:\\fake", InputProvider: provider);
        var result = await sut.ProjectAsync(req);

        _out.WriteLine($"Chain: {result.Chain.Count} nodes");
        foreach (var n in result.Chain)
            _out.WriteLine($"  {n.Date}: Depth={n.Depth}, GuneT={n.RawGuneTahsilat:N2}, " +
                $"DundenT={n.DundenTahsilat:N2}, OncekiT={n.OncekiTahsilat:N2}");

        Assert.True(result.Ok);
        // All 3 days have data + 1 NoData base case
        Assert.Equal(4, result.Chain.Count);

        // day1: GuneT=300, DundenT=0, OncekiT=0 (base case)
        // day2: GuneT=300, DundenT=300 (day1.GuneT), OncekiT=0
        // day3: GuneT=300, DundenT=300 (day2.GuneT), OncekiT=300 (prev.Onceki+prev.Dunden = 0+300)
        Assert.Equal(300m, result.GuneTahsilat);
        Assert.Equal(300m, result.DundenTahsilat);
        Assert.Equal(300m, result.OncekiTahsilat);
    }

    /// <summary>
    /// P2.1-T3: InputProvider source bilgisi ExcelAndSnapshot olarak işaretlenir.
    /// </summary>
    [Fact]
    public async Task P21_InputProvider_SourceIsExcelAndSnapshot()
    {
        var today = new DateOnly(2026, 4, 1);
        DayInputProvider provider = (DateOnly d, CancellationToken ct) =>
        {
            if (d != today) return Task.FromResult<ProjectionDayInput?>(null);
            return Task.FromResult<ProjectionDayInput?>(new ProjectionDayInput(
                d, 0, 0, 0, 0, 0, 0, 0, 0, 0, ProjectionDaySource.ExcelAndSnapshot));
        };

        var sut = CreateSut();
        var result = await sut.ProjectAsync(new ProjectionRequest(today, "C:\\fake", InputProvider: provider));

        Assert.True(result.Ok);
        Assert.Equal(ProjectionDaySource.ExcelAndSnapshot, result.Chain[0].Source);
    }
}
