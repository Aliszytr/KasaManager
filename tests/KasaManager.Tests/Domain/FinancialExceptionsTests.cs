using KasaManager.Domain.FinancialExceptions;
using KasaManager.Domain.Reports.Snapshots;
using Xunit;

namespace KasaManager.Tests.Domain;

/// <summary>
/// Financial Exceptions hardening testleri.
/// KB-1/KB-2/OB-5 bulgu kapatma + parity doğrulama.
/// </summary>
public sealed class FinancialExceptionsTests
{
    // ═══════════════════════════════════════
    // KB-1: BekleyenTutarCalculator overload parity
    // ═══════════════════════════════════════

    [Fact]
    public void Hesapla_EntityOverload_And_RawOverload_ProduceSameResult_BasarisizVirman()
    {
        var ex = CreateIstisna(IstisnaTuru.BasarisizVirman, beklenen: 5000m, gerceklesen: 3000m, sisteme: 0m);

        var entityResult = BekleyenTutarCalculator.Hesapla(ex);
        var rawResult = BekleyenTutarCalculator.Hesapla(ex.Tur, ex.BeklenenTutar, ex.GerceklesenTutar, ex.SistemeGirilenTutar);

        Assert.Equal(entityResult, rawResult);
        Assert.Equal(2000m, entityResult); // 5000 - 3000
    }

    [Fact]
    public void Hesapla_EntityOverload_And_RawOverload_ProduceSameResult_SistemeGirilmeyenEft()
    {
        var ex = CreateIstisna(IstisnaTuru.SistemeGirilmeyenEft, beklenen: 0m, gerceklesen: 7500m, sisteme: 2500m);

        var entityResult = BekleyenTutarCalculator.Hesapla(ex);
        var rawResult = BekleyenTutarCalculator.Hesapla(ex.Tur, ex.BeklenenTutar, ex.GerceklesenTutar, ex.SistemeGirilenTutar);

        Assert.Equal(entityResult, rawResult);
        Assert.Equal(5000m, entityResult); // 7500 - 2500
    }

    [Fact]
    public void Hesapla_EntityOverload_And_RawOverload_ProduceSameResult_KismiIslem()
    {
        var ex = CreateIstisna(IstisnaTuru.KismiIslem, beklenen: 10000m, gerceklesen: 0m, sisteme: 4000m);

        var entityResult = BekleyenTutarCalculator.Hesapla(ex);
        var rawResult = BekleyenTutarCalculator.Hesapla(ex.Tur, ex.BeklenenTutar, ex.GerceklesenTutar, ex.SistemeGirilenTutar);

        Assert.Equal(entityResult, rawResult);
        Assert.Equal(6000m, entityResult); // 10000 - 4000
    }

    [Theory]
    [InlineData(IstisnaTuru.BasarisizVirman)]
    [InlineData(IstisnaTuru.SistemeGirilmeyenEft)]
    [InlineData(IstisnaTuru.KismiIslem)]
    [InlineData(IstisnaTuru.BankadanCikamayanTutar)]
    [InlineData(IstisnaTuru.GecikmeliBankaHareketi)]
    public void Hesapla_NegativeResult_ClampedToZero(IstisnaTuru tur)
    {
        // Tüm türlerde negatif sonuç 0'a clamp edilmelidir
        var result = BekleyenTutarCalculator.Hesapla(tur, beklenenTutar: 100m, gerceklesenTutar: 500m, sistemeGirilenTutar: 500m);
        Assert.True(result >= 0m, $"Negatif sonuç: {result} — tür: {tur}");
    }

    // ═══════════════════════════════════════
    // KB-1: HistoricalState.EffectiveBekleyenTutar parity
    // ═══════════════════════════════════════

    [Fact]
    public void HistoricalState_EffectiveBekleyenTutar_Uses_TypeSpecificFormula()
    {
        // SistemeGirilmeyenEft: formül = GerceklesenTutar - SistemeGirilenTutar
        // Basit delta: BeklenenTutar - SistemeGirilenTutar farklı sonuç verirdi
        var state = new HistoricalState(
            Existed: true,
            Tur: IstisnaTuru.SistemeGirilmeyenEft,
            KararDurumu: KararDurumu.Onaylandi,
            Durum: IstisnaDurumu.Acik,
            BeklenenTutar: 1000m,  // Basit delta kullanılsaydı: 1000 - 200 = 800
            GerceklesenTutar: 5000m,
            SistemeGirilenTutar: 200m,
            Explanation: "test");

        // Tür-spesifik formül: GerceklesenTutar - SistemeGirilenTutar = 5000 - 200 = 4800
        Assert.Equal(4800m, state.EffectiveBekleyenTutar);
        // Eski basit delta doğru olsaydı 800 olurdu — bu test parity'nin doğru olduğunu kanıtlıyor
        Assert.NotEqual(800m, state.EffectiveBekleyenTutar);
    }

    [Fact]
    public void HistoricalState_EffectiveBekleyenTutar_ParityWith_RuntimeCalculation()
    {
        var ex = CreateIstisna(IstisnaTuru.BankadanCikamayanTutar, beklenen: 3000m, gerceklesen: 1000m, sisteme: 500m);

        var runtimeResult = BekleyenTutarCalculator.Hesapla(ex);

        var historicalState = new HistoricalState(
            Existed: true,
            Tur: ex.Tur,
            KararDurumu: KararDurumu.Onaylandi,
            Durum: IstisnaDurumu.Acik,
            BeklenenTutar: ex.BeklenenTutar,
            GerceklesenTutar: ex.GerceklesenTutar,
            SistemeGirilenTutar: ex.SistemeGirilenTutar,
            Explanation: "test");

        // Runtime ile historical parity
        Assert.Equal(runtimeResult, historicalState.EffectiveBekleyenTutar);
    }

    [Fact]
    public void ResolveStateAt_PropagatesTur()
    {
        var ex = CreateIstisna(IstisnaTuru.GecikmeliBankaHareketi, beklenen: 1000m, gerceklesen: 0m, sisteme: 0m);
        var history = Array.Empty<FinansalIstisnaHistory>();

        var state = HistoricalEffectiveStateResolver.ResolveStateAt(
            ex, DateOnly.FromDateTime(DateTime.Today.AddDays(1)), history);

        Assert.True(state.Existed);
        Assert.Equal(IstisnaTuru.GecikmeliBankaHareketi, state.Tur);
    }

    // ═══════════════════════════════════════
    // OB-5: CalculatedKasaSnapshot typed catch
    // ═══════════════════════════════════════

    [Fact]
    public void GetInputs_InvalidJson_StillReturnsDefaults()
    {
        var snapshot = new CalculatedKasaSnapshot { InputsJson = "{ broken invalid json" };
        var result = snapshot.GetInputs();

        Assert.NotNull(result);
        Assert.NotEmpty(result); // MissingFieldHandler defaults should be populated
    }

    [Fact]
    public void GetOutputs_InvalidJson_StillReturnsDefaults()
    {
        var snapshot = new CalculatedKasaSnapshot { OutputsJson = "not json at all <<<" };
        var result = snapshot.GetOutputs();

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    // ═══════════════════════════════════════
    // FinancialExceptionsSummary typed catch
    // ═══════════════════════════════════════

    [Fact]
    public void FromJson_InvalidJson_ReturnsNull()
    {
        var result = FinancialExceptionsSummary.FromJson("not valid json {{{");
        Assert.Null(result);
    }

    [Fact]
    public void FromJson_ValidJson_ReturnsObject()
    {
        var summary = new FinancialExceptionsSummary { ActiveCount = 3, TotalEffect = 1500m };
        var json = summary.ToJson();
        var result = FinancialExceptionsSummary.FromJson(json);

        Assert.NotNull(result);
        Assert.Equal(3, result.ActiveCount);
        Assert.Equal(1500m, result.TotalEffect);
    }

    // ═══════════════════════════════════════
    // IsRuntimeEffective guard tests
    // ═══════════════════════════════════════

    [Theory]
    [InlineData(KararDurumu.IncelemeBekliyor, IstisnaDurumu.Acik, false)]
    [InlineData(KararDurumu.Reddedildi, IstisnaDurumu.Acik, false)]
    [InlineData(KararDurumu.Onaylandi, IstisnaDurumu.Cozuldu, false)]
    [InlineData(KararDurumu.Onaylandi, IstisnaDurumu.Iptal, false)]
    [InlineData(KararDurumu.Onaylandi, IstisnaDurumu.ErtesiGuneDevredildi, false)]
    [InlineData(KararDurumu.Onaylandi, IstisnaDurumu.Acik, true)]
    [InlineData(KararDurumu.Onaylandi, IstisnaDurumu.KismiCozuldu, true)]
    public void IsRuntimeEffective_GuardMatrix(KararDurumu karar, IstisnaDurumu durum, bool expected)
    {
        var ex = CreateIstisna(IstisnaTuru.BasarisizVirman, 1000m, 0m, 0m);
        ex.KararDurumu = karar;
        ex.Durum = durum;

        Assert.Equal(expected, BekleyenTutarCalculator.IsRuntimeEffective(ex));
    }

    // ═══════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════

    private static FinansalIstisna CreateIstisna(IstisnaTuru tur, decimal beklenen, decimal gerceklesen, decimal sisteme)
    {
        return new FinansalIstisna
        {
            Id = Guid.NewGuid(),
            IslemTarihi = DateOnly.FromDateTime(DateTime.Today),
            Tur = tur,
            Kategori = IstisnaKategorisi.BekleyenSistemGirisi,
            HesapTuru = KasaManager.Domain.Reports.HesapKontrol.BankaHesapTuru.Tahsilat,
            BeklenenTutar = beklenen,
            GerceklesenTutar = gerceklesen,
            SistemeGirilenTutar = sisteme,
            EtkiYonu = KasaEtkiYonu.Artiran,
            OlusturmaTarihiUtc = DateTime.UtcNow.AddDays(-1)
        };
    }
}
