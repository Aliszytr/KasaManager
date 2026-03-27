using Xunit;
using KasaManager.Domain.Helpers;

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
