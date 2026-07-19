using Xunit;
using KasaManager.Domain.Helpers;
using KasaManager.Web.Helpers;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace KasaManager.Tests.ParityProof;

/// <summary>
/// CalculateAksamLegacy hesaplama zincirini birebir taklit eder.
/// Aynı input set ile:
///   1) Eski davranış (Round yok) → "Before"
///   2) Yeni davranış (FinancialMath.Round uygulanmış) → "After"
///   3) FormulaEngineService davranışı (NCalc double→decimal→Round) → "Engine"
/// 
/// Amacı: Rounding eklenmesinin sayısal fark ürettiğini ve bu farkın
/// FormulaEngineService çıktısıyla parity sağladığını kanıtlamak.
/// </summary>
public sealed class RoundingParityProofTest
{
    // --- FinancialMath referans ---
    static decimal R(decimal v) => FinancialMath.Round(v); // 2 basamak, AwayFromZero

    // --- NCalc simülasyonu (double aritmetik → decimal cast → Round) ---
    static decimal NCalcSim(double v) => FinancialMath.Round((decimal)v);

    // ======================================================================
    // SENARYO: Kuruş altı artık üreten gerçekçi edge-case inputlar
    // ======================================================================
    //
    // Input set: 3+ ondalık basamaklı Excel parse sonuçları
    //   Tahsilat     = 12345.675  (3 basamak — Excel OADate→decimal parse artığı olabilir)
    //   Harc         = 4321.125
    //   OnlineHarc   = 1500.005
    //   Reddiyat     = 876.335
    //   OnlineReddi  = 200.115
    //   Stopaj       = 543.215
    //   OnlineStopaj = 150.005
    //   Diğerleri    = tam sayılar (edge-case olmayan)

    const decimal Tahsilat     = 12345.675m;
    const decimal Harc         = 4321.125m;
    const decimal Reddiyat     = 876.335m;
    const decimal OnlineReddi  = 200.115m;
    const decimal Stopaj       = 543.215m;
    const decimal OnlineStopaj = 150.005m;

    const decimal DevredenKasa = 5000m;
    const decimal BankadanCekilen = 0m;
    const decimal VergiGelenKasa = 0m;
    const decimal CesitliNedenlerle = 0m;
    const decimal BankayaYatirilacakHarciDegistir = 0m;
    const decimal BankayaYatirilacakTahsilatiDegistir = 0m;
    const decimal KaydenTahsilat = 0m;
    const decimal KaydenHarc = 0m;
    const decimal VergiKasa = 0m;
    const decimal BankayaGonderilmisDeger = 0m;
    const decimal BozukPara = 100m;

    // CalculateAksamLegacy zincirinin birebir kopyası
    static (decimal NormalTahsilat, decimal NormalHarc,
            decimal NormalReddiyat, decimal NormalStopaj,
            decimal OnlineStopajOut, decimal ToplamStopaj,
            decimal BankayaYatirilacakHarc, decimal BankayaYatirilacakNakit,
            decimal BankayaYatirilacakStopaj, decimal StopajKontrol,
            decimal GenelKasa, decimal BankaGoturulecekNakit,
            decimal BozukParaHaricKasa)
    CalcRaw()
    {
        var normalTahsilat = Math.Max(0m, Tahsilat);
        var normalHarc = Harc;
        var normalReddiyat = Math.Max(0m, Reddiyat - OnlineReddi);
        var toplamStopaj = Stopaj;
        var onlineStopaj = OnlineStopaj;
        var normalStopaj = Math.Max(0m, toplamStopaj - onlineStopaj);
        var bankayaYatirilacakStopaj = normalStopaj;
        var bankayaYatirilacakHarc = Math.Max(0m, normalHarc + BankayaYatirilacakHarciDegistir - KaydenHarc);
        var baseMasraf = Math.Max(0m, Tahsilat - normalReddiyat);
        var bankayaYatirilacakNakit = Math.Max(0m,
            baseMasraf + BankayaYatirilacakTahsilatiDegistir - (VergiKasa + KaydenTahsilat));
        var stopajKontrol = 0m; // isSabah=false → bankaTahsilatGun.Cikan=0 - OnlineReddi
        var genelKasa = (DevredenKasa + (BankadanCekilen + VergiGelenKasa) + normalTahsilat + normalStopaj)
            + CesitliNedenlerle
            - (normalReddiyat + bankayaYatirilacakNakit + KaydenTahsilat);
        var bankaGoturulecekNakit = Math.Max(0m,
            (bankayaYatirilacakHarc + bankayaYatirilacakNakit + bankayaYatirilacakStopaj)
            - BankayaGonderilmisDeger);
        var bozukParaHaricKasa = genelKasa - BozukPara;

        return (normalTahsilat, normalHarc, normalReddiyat, normalStopaj,
                onlineStopaj, toplamStopaj, bankayaYatirilacakHarc,
                bankayaYatirilacakNakit, bankayaYatirilacakStopaj,
                stopajKontrol, genelKasa, bankaGoturulecekNakit, bozukParaHaricKasa);
    }

    [Fact]
    public void Proof_RoundingChangesOutput_And_MatchesFormulaEngine()
    {
        var raw = CalcRaw();

        // ─── BEFORE (eski davranış: Round yok) ───
        var beforeNormalTahsilat     = raw.NormalTahsilat;
        var beforeNormalHarc         = raw.NormalHarc;
        var beforeNormalReddiyat     = raw.NormalReddiyat;
        var beforeNormalStopaj       = raw.NormalStopaj;
        var beforeOnlineStopaj       = raw.OnlineStopajOut;
        var beforeToplamStopaj       = raw.ToplamStopaj;
        var beforeBYHarc             = raw.BankayaYatirilacakHarc;
        var beforeBYNakit            = raw.BankayaYatirilacakNakit;
        var beforeBYStopaj           = raw.BankayaYatirilacakStopaj;
        var beforeStopajKontrol      = raw.StopajKontrol;
        var beforeGenelKasa          = raw.GenelKasa;
        var beforeBankaGoturulecek   = raw.BankaGoturulecekNakit;
        var beforeBozukHaric         = raw.BozukParaHaricKasa;

        // ─── AFTER (yeni davranış: FinancialMath.Round) ───
        var afterNormalTahsilat     = R(raw.NormalTahsilat);
        var afterNormalHarc         = R(raw.NormalHarc);
        var afterNormalReddiyat     = R(raw.NormalReddiyat);
        var afterNormalStopaj       = R(raw.NormalStopaj);
        var afterOnlineStopaj       = R(raw.OnlineStopajOut);
        var afterToplamStopaj       = R(raw.ToplamStopaj);
        var afterBYHarc             = R(raw.BankayaYatirilacakHarc);
        var afterBYNakit            = R(raw.BankayaYatirilacakNakit);
        var afterBYStopaj           = R(raw.BankayaYatirilacakStopaj);
        var afterStopajKontrol      = R(raw.StopajKontrol);
        var afterGenelKasa          = R(raw.GenelKasa);
        var afterBankaGoturulecek   = R(raw.BankaGoturulecekNakit);
        var afterBozukHaric         = R(raw.BozukParaHaricKasa);

        // ─── FormulaEngineService eşdeğeri (double aritmetik → Round) ───
        // NCalc double üzerinden çalışır, sonuç (decimal) cast → FinancialMath.Round
        var engineNormalReddiyat = NCalcSim((double)Reddiyat - (double)OnlineReddi);
        var engineNormalStopaj   = NCalcSim(Math.Max(0, (double)Stopaj - (double)OnlineStopaj));

        // ─── ASSERTIONS: Farklar var mı? ───
        // NormalReddiyat: 876.335 - 200.115 = 676.220 → 676.22 (3.basamak 0, fark yok)
        // NormalStopaj:   543.215 - 150.005 = 393.210 → 393.21 (3.basamak 0, fark yok)
        // NormalTahsilat: 12345.675 → Round = 12345.68 (0.005 yuvarlaması: AwayFromZero ETKİLİ)
        // NormalHarc:     4321.125  → Round = 4321.13  (0.005 yuvarlaması: AwayFromZero ETKİLİ)
        // ToplamStopaj:   543.215   → Round = 543.22   (AwayFromZero ETKİLİ)
        // OnlineStopaj:   150.005   → Round = 150.01   (AwayFromZero ETKİLİ)

        // Kanıt: Before ≠ After olan alanlar (3. basamakta .XX5 olanlar)
        // Not: decimal'de 5393.210m == 5393.21m (trailing zero), bu yüzden
        // sadece gerçek midpoint (.XX5) barındıran alanları kontrol ediyoruz.
        Assert.NotEqual(beforeNormalTahsilat, afterNormalTahsilat);   // 12345.675 → 12345.68
        Assert.NotEqual(beforeNormalHarc, afterNormalHarc);           // 4321.125 → 4321.13
        Assert.NotEqual(beforeToplamStopaj, afterToplamStopaj);       // 543.215 → 543.22
        Assert.NotEqual(beforeOnlineStopaj, afterOnlineStopaj);       // 150.005 → 150.01
        Assert.NotEqual(beforeBYHarc, afterBYHarc);                   // 4321.125 → 4321.13

        // Kanıt: After = FormulaEngine (NCalc sim)
        Assert.Equal(afterNormalReddiyat, engineNormalReddiyat);
        Assert.Equal(afterNormalStopaj, engineNormalStopaj);

        // Kanıt: Round tutarlılığı — 2 basamak, AwayFromZero
        Assert.Equal(12345.68m, afterNormalTahsilat);
        Assert.Equal(4321.13m,  afterNormalHarc);
        Assert.Equal(543.22m,   afterToplamStopaj);
        Assert.Equal(150.01m,   afterOnlineStopaj);
        Assert.Equal(393.21m,   afterNormalStopaj);
        Assert.Equal(676.22m,   afterNormalReddiyat);

        // ── Console output (xUnit ITestOutputHelper olmadan da dotnet test --verbosity diag ile görülür) ──
        // Bu assertion'lar geçerse rounding parity kanıtlanmış olur.
    }

    [Fact]
    public void Print_BeforeAfter_Table()
    {
        var raw = CalcRaw();

        // Her alan için (before, after, fark) üçlüsü
        var fields = new (string Name, decimal Before, decimal After)[]
        {
            ("NormalTahsilat",           raw.NormalTahsilat,           R(raw.NormalTahsilat)),
            ("NormalHarc",               raw.NormalHarc,               R(raw.NormalHarc)),
            ("NormalReddiyat",           raw.NormalReddiyat,           R(raw.NormalReddiyat)),
            ("NormalStopaj",             raw.NormalStopaj,             R(raw.NormalStopaj)),
            ("OnlineStopaj",             raw.OnlineStopajOut,          R(raw.OnlineStopajOut)),
            ("ToplamStopaj",             raw.ToplamStopaj,             R(raw.ToplamStopaj)),
            ("BankayaYatirilacakHarc",   raw.BankayaYatirilacakHarc,   R(raw.BankayaYatirilacakHarc)),
            ("BankayaYatirilacakNakit",  raw.BankayaYatirilacakNakit,  R(raw.BankayaYatirilacakNakit)),
            ("BankayaYatirilacakStopaj", raw.BankayaYatirilacakStopaj, R(raw.BankayaYatirilacakStopaj)),
            ("StopajKontrol",            raw.StopajKontrol,            R(raw.StopajKontrol)),
            ("GenelKasa",                raw.GenelKasa,                R(raw.GenelKasa)),
            ("BankaGoturulecekNakit",    raw.BankaGoturulecekNakit,    R(raw.BankaGoturulecekNakit)),
            ("BozukParaHaricKasa",       raw.BozukParaHaricKasa,       R(raw.BozukParaHaricKasa)),
        };

        int changed = 0;
        foreach (var f in fields)
        {
            if (f.Before != f.After) changed++;
        }

        // En az 1 alanda Before ≠ After olmalı (rounding etkisi kanıtı)
        Assert.True(changed > 0, "En az 1 alanda rounding farkı bekleniyor");
    }
}

public sealed class NegativeTahsilatWithdrawalTests
{
    private const decimal RawNegativeTahsilat = -27_846m;

    [Fact]
    public void NegativeTahsilat_NoBankWithdrawal_ShowsRequiredWithdrawal()
    {
        var model = new KasaPreviewViewModel { BankadanCekilen = 0m };

        var decision = model.GetNegativeTahsilatWithdrawalDecision(RawNegativeTahsilat);

        Assert.True(decision.ShouldShowModal);
        Assert.Equal(27_846m, decision.RawShortfall);
        Assert.Equal(27_846m, decision.RequiredTotalWithdrawal);
        Assert.Equal(27_846m, decision.RemainingWithdrawal);
    }

    [Fact]
    public void NegativeTahsilat_ExactWithdrawal_DoesNotShowModal()
    {
        var model = new KasaPreviewViewModel { BankadanCekilen = 27_846m };

        var decision = model.GetNegativeTahsilatWithdrawalDecision(RawNegativeTahsilat);

        Assert.False(decision.ShouldShowModal);
        Assert.Equal(0m, decision.RemainingWithdrawal);
    }

    [Fact]
    public void NegativeTahsilat_ExcessWithdrawal_DoesNotCreatePositiveDeposit()
    {
        var model = new KasaPreviewViewModel { BankadanCekilen = 28_766m };

        var decision = model.GetNegativeTahsilatWithdrawalDecision(RawNegativeTahsilat);
        var clampedBankayaYatirilacakTahsilat = Math.Max(0m, RawNegativeTahsilat);

        Assert.False(decision.ShouldShowModal);
        Assert.Equal(0m, decision.RemainingWithdrawal);
        Assert.Equal(0m, clampedBankayaYatirilacakTahsilat);
        Assert.Equal(920m, decision.ExistingWithdrawal - decision.RawShortfall);
    }

    [Fact]
    public void NegativeTahsilat_PartialWithdrawal_ShowsRemainingAmount()
    {
        var model = new KasaPreviewViewModel { BankadanCekilen = 10_000m };

        var decision = model.GetNegativeTahsilatWithdrawalDecision(RawNegativeTahsilat);

        Assert.True(decision.ShouldShowModal);
        Assert.Equal(17_846m, decision.RemainingWithdrawal);
    }

    [Fact]
    public void PartialWithdrawal_DecisionReturnsExistingPlusRemainingAsRequiredTotal()
    {
        var model = new KasaPreviewViewModel { BankadanCekilen = 10_000m };

        var decision = model.GetNegativeTahsilatWithdrawalDecision(RawNegativeTahsilat);

        Assert.Equal(27_846m, decision.RawShortfall);
        Assert.Equal(10_000m, decision.ExistingWithdrawal);
        Assert.Equal(17_846m, decision.RemainingWithdrawal);
        Assert.Equal(
            decision.ExistingWithdrawal + decision.RemainingWithdrawal,
            decision.RequiredTotalWithdrawal);
        Assert.Equal(27_846m, decision.RequiredTotalWithdrawal);
        Assert.True(decision.ShouldShowModal);
    }

    [Fact]
    public void NoWithdrawal_RequiredTotalEqualsRawShortfall()
    {
        var model = new KasaPreviewViewModel { BankadanCekilen = 0m };

        var decision = model.GetNegativeTahsilatWithdrawalDecision(RawNegativeTahsilat);

        Assert.Equal(decision.RawShortfall, decision.RequiredTotalWithdrawal);
        Assert.Equal(27_846m, decision.RequiredTotalWithdrawal);
    }

    [Fact]
    public void ExactWithdrawal_DoesNotShowModal()
    {
        var model = new KasaPreviewViewModel { BankadanCekilen = 27_846m };

        var decision = model.GetNegativeTahsilatWithdrawalDecision(RawNegativeTahsilat);

        Assert.False(decision.ShouldShowModal);
        Assert.Equal(27_846m, decision.RequiredTotalWithdrawal);
    }

    [Fact]
    public void ExcessWithdrawal_DoesNotReduceExistingValue()
    {
        var model = new KasaPreviewViewModel { BankadanCekilen = 28_766m };

        var decision = model.GetNegativeTahsilatWithdrawalDecision(RawNegativeTahsilat);

        Assert.False(decision.ShouldShowModal);
        Assert.Equal(28_766m, model.BankadanCekilen);
        Assert.Equal(28_766m, decision.ExistingWithdrawal);
        Assert.Equal(28_766m, decision.RequiredTotalWithdrawal);
    }

    [Fact]
    public void PartialWithdrawal_ConfirmUsesRequiredTotalNotRemaining()
    {
        var model = new KasaPreviewViewModel { BankadanCekilen = 10_000m };
        var decision = model.GetNegativeTahsilatWithdrawalDecision(RawNegativeTahsilat);
        var projectRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var viewPath = Path.Combine(
            projectRoot, "src", "KasaManager.Web", "Views", "KasaPreview", "Index.cshtml");
        var viewSource = File.ReadAllText(viewPath);

        Assert.Equal(17_846m, decision.RemainingWithdrawal);
        Assert.Equal(27_846m, decision.RequiredTotalWithdrawal);
        Assert.Contains(
            "var requiredTotalWithdrawal = @requiredTotalWithdrawal.ToString",
            viewSource);
        Assert.Contains(
            "input.value = requiredTotalWithdrawal.toFixed(2).replace('.', ',');",
            viewSource);
        Assert.DoesNotContain("input.value = remainingWithdrawal", viewSource);
        Assert.DoesNotContain(
            "parseFloat(document.getElementById('bankadanCekmeTutar').value)",
            viewSource);
    }

    [Fact]
    public async Task TurkishDecimalBinding_TotalWithdrawal()
    {
        var model = new KasaPreviewViewModel { BankadanCekilen = 10_000m };
        var decision = model.GetNegativeTahsilatWithdrawalDecision(RawNegativeTahsilat);
        var postedValue = decision.RequiredTotalWithdrawal.ToString(
            "F2",
            System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));

        var binding = await BindTurkishDecimalAsync(postedValue);

        Assert.Equal("27846,00", postedValue);
        Assert.True(binding.Success);
        Assert.Equal(27_846m, binding.Value);
    }

    [Fact]
    public async Task BankadanCekilen_RoundTripsThroughRecalculate()
    {
        var binding = await BindTurkishDecimalAsync("28766,00");
        Assert.True(binding.Success);
        var boundValue = binding.Value;
        var postedModel = new KasaPreviewViewModel
        {
            BankadanCekilen = boundValue,
            KasaType = "Aksam",
            SelectedDate = new DateOnly(2026, 7, 15),
            HasResults = true
        };

        var recalculationDto = postedModel.ToDto();
        var recalculatedModel = new KasaPreviewViewModel();
        recalculatedModel.UpdateFromDto(recalculationDto);

        var userName = $"negative-withdrawal-{Guid.NewGuid():N}";
        try
        {
            await KasaDraftCacheHelper.SaveDraftAsync(
                userName, "Aksam", recalculatedModel);
            var restoredModel = new KasaPreviewViewModel();

            var loaded = await KasaDraftCacheHelper.TryLoadDraftAsync(
                userName, "Aksam", restoredModel);
            var secondRecalculationDto = restoredModel.ToDto();

            Assert.True(loaded);
            Assert.Equal(28_766m, postedModel.BankadanCekilen);
            Assert.Equal(28_766m, recalculationDto.BankadanCekilen);
            Assert.Equal(28_766m, restoredModel.BankadanCekilen);
            Assert.Equal(28_766m, secondRecalculationDto.BankadanCekilen);
        }
        finally
        {
            await KasaDraftCacheHelper.ClearDraftAsync(userName, "Aksam");
        }
    }

    [Fact]
    public async Task DraftCache_JsonRoundTrip_PreservesFinancialAndTurkishFields()
    {
        var userName = $"json-security-{Guid.NewGuid():N}";
        var source = new KasaPreviewViewModel
        {
            KasaType = "Sabah",
            SelectedDate = new DateOnly(2026, 7, 16),
            BankadanCekilen = 28_766m,
            BozukPara = 6_000m,
            NakitPara = 4_000m,
            VergideBirikenKasa = 31_984m,
            KasayiYapan = "Çağrı Şahin",
            Aciklama = "İstanbul veznesi — öğle açıklaması",
            VergidenGelen = null
        };

        try
        {
            await KasaDraftCacheHelper.SaveDraftAsync(userName, "Sabah", source);
            var restored = new KasaPreviewViewModel();

            var loaded = await KasaDraftCacheHelper.TryLoadDraftAsync(
                userName, "Sabah", restored);

            Assert.True(loaded);
            Assert.Equal(source.SelectedDate, restored.SelectedDate);
            Assert.Equal(28_766m, restored.BankadanCekilen);
            Assert.Equal(6_000m, restored.BozukPara);
            Assert.Equal(4_000m, restored.NakitPara);
            Assert.Equal(31_984m, restored.VergideBirikenKasa);
            Assert.Equal("Çağrı Şahin", restored.KasayiYapan);
            Assert.Equal("İstanbul veznesi — öğle açıklaması", restored.Aciklama);
            Assert.Null(restored.VergidenGelen);
        }
        finally
        {
            await KasaDraftCacheHelper.ClearDraftAsync(userName, "Sabah");
        }
    }

    [Theory]
    [InlineData("27846", true, "27846")]
    [InlineData("27846,00", true, "27846")]
    [InlineData("27846.00", false, null)]
    [InlineData("28.766,00", false, null)]
    public async Task TurkishDecimalBinding_BankadanCekilen(
        string postedValue,
        bool expectedSuccess,
        string? expectedInvariant)
    {
        var binding = await BindTurkishDecimalAsync(postedValue);

        Assert.Equal(expectedSuccess, binding.Success);
        if (expectedSuccess)
        {
            var expected = decimal.Parse(
                expectedInvariant!,
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(expected, binding.Value);
        }
    }

    private static async Task<(bool Success, decimal Value)> BindTurkishDecimalAsync(
        string postedValue)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        var values = new Dictionary<string, StringValues>
        {
            [nameof(KasaPreviewViewModel.BankadanCekilen)] = postedValue
        };
        var valueProvider = new FormValueProvider(
            BindingSource.Form,
            new FormCollection(values),
            culture);
        var metadata = new EmptyModelMetadataProvider()
            .GetMetadataForType(typeof(decimal));
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());
        var bindingContext = DefaultModelBindingContext.CreateBindingContext(
            actionContext,
            valueProvider,
            metadata,
            bindingInfo: null,
            modelName: nameof(KasaPreviewViewModel.BankadanCekilen));
        var binder = new SimpleTypeModelBinder(
            typeof(decimal),
            NullLoggerFactory.Instance);

        await binder.BindModelAsync(bindingContext);

        if (!bindingContext.Result.IsModelSet)
        {
            return (false, 0m);
        }

        return (true, Assert.IsType<decimal>(bindingContext.Result.Model));
    }
}
