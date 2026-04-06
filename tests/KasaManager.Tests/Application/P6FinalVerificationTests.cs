using System.Collections.Concurrent;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Observability;
using KasaManager.Application.Services;
using KasaManager.Domain.Projection;
using KasaManager.Domain.Reports.HesapKontrol;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace KasaManager.Tests.Application;

/// <summary>
/// P6 FINAL VERIFICATION & PRODUCTION LOCK Tests
/// </summary>
public sealed class P6FinalVerificationTests
{
    private readonly ITestOutputHelper _out;
    private readonly Mock<IBankaHesapKontrolService> _hkMock = new();
    private readonly Mock<IAlertService> _alertMock = new();
    private readonly Mock<ILogger<EksikFazlaProjectionEngine>> _loggerMock = new();

    public P6FinalVerificationTests(ITestOutputHelper output)
    {
        _out = output;
        _hkMock.Setup(h => h.GetHistoryAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), 
            It.IsAny<BankaHesapTuru?>(), It.IsAny<KayitDurumu?>(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HesapKontrolKaydi>());
    }

    private EksikFazlaProjectionEngine CreateEngine()
    {
        return new EksikFazlaProjectionEngine(_hkMock.Object, _loggerMock.Object, _alertMock.Object);
    }

    // GÖREV 1 — DETERMINISTIC REPLAY TEST
    [Fact]
    public async Task T1_Deterministic_Replay_Produces_Exact_Same_Results()
    {
        var targetDate = new DateOnly(2026, 4, 3);
        var engine = CreateEngine();
        
        DayInputProvider provider = (d, ct) => Task.FromResult<ProjectionDayInput?>(
            new ProjectionDayInput(d, 500, 100, 1000, 200, 300, 50, 40, 800, 250, ProjectionDaySource.ExcelAndSnapshot));

        var req = new ProjectionRequest(targetDate, "fake_folder", InputProvider: provider);

        var jsonOptions = new JsonSerializerOptions { WriteIndented = false };

        var run1 = await engine.ProjectAsync(req);
        var str1 = JsonSerializer.Serialize(run1, jsonOptions);
        
        var run2 = await engine.ProjectAsync(req);
        var str2 = JsonSerializer.Serialize(run2, jsonOptions);
        
        var run3 = await engine.ProjectAsync(req);
        var str3 = JsonSerializer.Serialize(run3, jsonOptions);

        _out.WriteLine($"Run 1 Hash: {str1.GetHashCode()}");
        _out.WriteLine($"Run 2 Hash: {str2.GetHashCode()}");
        _out.WriteLine($"Run 3 Hash: {str3.GetHashCode()}");

        Assert.True(run1.Ok);
        Assert.Equal(str1, str2);
        Assert.Equal(str2, str3);
        _out.WriteLine("GÖREV 1: Deterministic Replay TEST PASSED! 3 consecutive runs are globally identical.");
    }

    // GÖREV 2 — INPUT GAP TEST
    [Fact]
    public async Task T2_InputGap_Raises_Alert_And_Metrics()
    {
        var targetDate = new DateOnly(2026, 4, 3);
        var engine = CreateEngine();

        long initialMissing = ShadowMetrics.MissingInputCount;

        // InputProvider returning NULL triggers Input Gap
        DayInputProvider nullProvider = (d, ct) => Task.FromResult<ProjectionDayInput?>(null);

        var req = new ProjectionRequest(targetDate, "fake_folder", InputProvider: nullProvider);
        var run = await engine.ProjectAsync(req);

        long afterMissing = ShadowMetrics.MissingInputCount;

        _out.WriteLine($"Missing Inputs Initial: {initialMissing}, After: {afterMissing}");

        // Result fail/empty verification
        // Node has NoData -> breaks chain
        Assert.True(run.Ok); // ProjectAsync itself does not crash, it returns Ok=true but chain node has HasData=false
        Assert.Single(run.Chain);
        Assert.False(run.Chain[0].HasData);

        // AlertService verification
        _alertMock.Verify(a => a.TriggerAsync("SHADOW_INPUT_MISSING", It.Is<string>(s => s.Contains("girdi verisi (InputProvider) bulamadı"))), Times.Once);

        // Metrics verification
        Assert.True(afterMissing > initialMissing);
        
        _out.WriteLine("GÖREV 2: Input Gap TEST PASSED! Alert successfully triggered, metric successfully bumped, silent zeroing avoided.");
    }

    // GÖREV 3 — LOAD TEST (LIGHT)
    [Fact]
    public async Task T3_Concurrent_Load_Test_Scales_Thread_Safe()
    {
        var targetDate = new DateOnly(2026, 4, 3);
        var engine = CreateEngine();
        
        DayInputProvider staticProvider = (d, ct) => Task.FromResult<ProjectionDayInput?>(
            new ProjectionDayInput(d, 100, 10, 1000, 200, 300, 50, 40, 800, 250, ProjectionDaySource.ExcelAndSnapshot));

        var req = new ProjectionRequest(targetDate, "fake", 3, staticProvider);

        long intialSuccess = ShadowMetrics.SuccessCount;

        var tasks = new List<Task<ProjectionResult>>();
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(Task.Run(() => engine.ProjectAsync(req)));
        }

        var results = await Task.WhenAll(tasks);

        long afterSuccess = ShadowMetrics.SuccessCount;

        _out.WriteLine($"Success Metric Start: {intialSuccess}, End: {afterSuccess}");

        // Each execution resolves 4 days (depth 3) = 4 * 20 = 80 increments expected
        long expectedDiff = 80;
        
        foreach(var res in results) Assert.True(res.Ok);
        Assert.Equal(expectedDiff, afterSuccess - intialSuccess);
        
        _out.WriteLine("GÖREV 3: Load TEST PASSED! 20 parallel computations caused 0 deadlocks or exceptions.");
    }

    // GÖREV 4 — CACHE CONSISTENCY TEST
    [Fact]
    public async Task T4_Repeated_Invocations_Are_Consistent()
    {
        var targetDate = new DateOnly(2026, 4, 3);
        var engine = CreateEngine();

        DayInputProvider provider = (d, ct) => Task.FromResult<ProjectionDayInput?>(
            new ProjectionDayInput(d, 600, 100, 2000, 50, 250, 30, 20, 1800, 200, ProjectionDaySource.ExcelAndSnapshot));

        var req = new ProjectionRequest(targetDate, "fake", 3, provider);

        var first = await engine.ProjectAsync(req);
        
        await Task.Delay(100); // Wait bit

        var second = await engine.ProjectAsync(req);

        Assert.Equal(first.GuneTahsilat, second.GuneTahsilat);
        Assert.Equal(first.GuneHarc, second.GuneHarc);

        _out.WriteLine("GÖREV 4: Cache Consistency (Repeated Time/Inv) TEST PASSED!");
    }

    // GÖREV 5 — SHADOW vs PROD PARITY
    [Fact]
    public async Task T5_Parity_5_Days_Verification()
    {
        var targetDate = new DateOnly(2026, 4, 3);
        var engine = CreateEngine();

        // Let's create an input provider that generates 5 distinct days of mock legacy input
        var days = new Dictionary<DateOnly, ProjectionDayInput>();
        for(int i=0; i<5; i++)
        {
            var d = targetDate.AddDays(-i);
            days[d] = new ProjectionDayInput(d, 500+i*10, 100+i, 2000+i*100, 200+i*5, 300+i*20, 50+i, 40+i, 1800, 200, ProjectionDaySource.ExcelAndSnapshot);
        }

        DayInputProvider provider = (d, ct) => Task.FromResult<ProjectionDayInput?>(days.ContainsKey(d) ? days[d] : null);

        var req = new ProjectionRequest(targetDate, "fake", 4, provider); // 4 lookback = 5 days exactly
        
        var result = await engine.ProjectAsync(req);

        Assert.True(result.Ok);
        Assert.Equal(5, result.Chain.Count(c => c.HasData));

        foreach(var node in result.Chain)
        {
            if(!node.HasData) continue;

            var input = days[node.Date];
            // Compute expected by manual legacy replica:
            // guneTahsilat = BankaGiren - ((OnlineTahsilat - OnlineReddiyat) + (Tahsilat - BankayaYatirilacakNakit))
            var expGT = input.BankaGirenTahsilat - ((input.OnlineTahsilat - input.OnlineReddiyat) + (input.ToplamTahsilat - input.BankayaYatirilacakNakit));
            
            // guneHarc = BankaHarcGiren - (OnlineHarc + NormalHarc)
            var expGH = input.BankaGirenHarc - (input.OnlineHarc + input.NormalHarc);

            Assert.Equal(expGT, node.RawGuneTahsilat);
            Assert.Equal(expGH, node.RawGuneHarc);
            
            _out.WriteLine($"Day {node.Date} Parity Verified. Engine GT: {node.RawGuneTahsilat}, Legacy GT: {expGT}");
        }

        _out.WriteLine("GÖREV 5: Shadow vs Legacy Parity TEST PASSED! 5 days correctly mapped to legacy formulas 1:1.");
    }
}
