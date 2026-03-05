#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace KasaManager.Application.Services.Draft.Calculators;

/// <summary>
/// R19: Sabah Kasa hesaplama mantığı.
/// Sabah Kasa, Akşam Kasa hesaplamalarını temel alır ve ek Eksik/Fazla taşıma mantığı içerir.
/// </summary>
public static class SabahKasaCalculator
{
    /// <summary>
    /// Sabah Kasa hesaplama sonuç kaydı.
    /// Akşam Kasa sonuçlarını + Eksik/Fazla taşıma değerlerini içerir.
    /// </summary>
    public sealed record SabahKasaResult(
        // Akşam Kasa'dan miras alınan değerler
        decimal NormalTahsilat,
        decimal NormalHarc,
        decimal NormalReddiyat,
        decimal NormalStopaj,
        decimal OnlineStopaj,
        decimal ToplamStopaj,
        decimal BankayaYatirilacakHarc,
        decimal BankayaYatirilacakNakit,
        decimal BankayaYatirilacakStopaj,
        decimal GenelKasa,
        decimal BankaGoturulecekNakit,
        decimal BozukParaHaricKasa,
        // Sabah Kasa'ya özgü değerler
        decimal StopajKontrol, // Sabah için sabit 0
        // Eksik/Fazla Tahsilat
        decimal GuneAitEksikFazlaTahsilat,
        decimal DundenEksikFazlaTahsilat,
        decimal OncekiGuneAitEksikFazlaTahsilat,
        // Eksik/Fazla Harç
        decimal GuneAitEksikFazlaHarc,
        decimal DundenEksikFazlaHarc,
        decimal OncekiGuneAitEksikFazlaHarc);

    /// <summary>
    /// Sabah Kasa hesaplama girdi parametreleri.
    /// </summary>
    public sealed record SabahKasaInputs(
        // Akşam Kasa girdileri
        AksamKasaCalculator.AksamKasaInputs AksamInputs,
        // Eksik/Fazla taşıma girdileri (önceki günden)
        decimal BankaGiren,
        decimal BankaHarcGiren,
        decimal OnlineTahsilat,
        decimal OnlineHarc,
        decimal ToplamHarc,
        // Önceki gün değerleri
        decimal PreviousDayGuneAitEksikFazlaTahsilat,
        decimal PreviousDayOncekiEksikFazlaTahsilat,
        decimal PreviousDayGuneAitEksikFazlaHarc,
        decimal PreviousDayOncekiEksikFazlaHarc);

    /// <summary>
    /// Sabah Kasa hesaplaması yapar.
    /// </summary>
    public static SabahKasaResult Calculate(SabahKasaInputs inputs)
    {
        // Önce Akşam Kasa hesapla
        var aksamResult = AksamKasaCalculator.Calculate(inputs.AksamInputs);

        // Sabah Kasa için StopajKontrol = 0 (sabit)
        const decimal stopajKontrol = 0m;

        // --- Eksik/Fazla Tahsilat Hesaplamaları ---
        // Gün içi Ek.Faz T = BankayaGiren - ((OnlineTahsilat - OnlineReddiyat) + (ToplamTahsilat - BankayaYatirilacakNakit))
        var guneAitEksikFazlaTahsilat = inputs.BankaGiren - (
            (inputs.OnlineTahsilat - inputs.AksamInputs.OnlineReddiyat) +
            (inputs.AksamInputs.UstTahsilat - aksamResult.BankayaYatirilacakNakit));

        // Dünden Ek.Faz T = Önceki günün Gün içi değeri
        var dundenEksikFazlaTahsilat = inputs.PreviousDayGuneAitEksikFazlaTahsilat;

        // Önceki güne ait = Önceki günün (Önceki + Dünden) değeri
        var oncekiGuneAitEksikFazlaTahsilat = inputs.PreviousDayOncekiEksikFazlaTahsilat + inputs.PreviousDayGuneAitEksikFazlaTahsilat;

        // --- Eksik/Fazla Harç Hesaplamaları ---
        // Gün içi Ek.Faz H = BankaHarcGiren - (OnlineHarc + ToplamHarc)
        var guneAitEksikFazlaHarc = inputs.BankaHarcGiren - (inputs.OnlineHarc + inputs.ToplamHarc);

        // Dünden Ek.Faz H = Önceki günün Gün içi değeri
        var dundenEksikFazlaHarc = inputs.PreviousDayGuneAitEksikFazlaHarc;

        // Önceki güne ait = Önceki günün (Önceki + Dünden) değeri
        var oncekiGuneAitEksikFazlaHarc = inputs.PreviousDayOncekiEksikFazlaHarc + inputs.PreviousDayGuneAitEksikFazlaHarc;

        return new SabahKasaResult(
            // Akşam Kasa değerleri
            NormalTahsilat: aksamResult.NormalTahsilat,
            NormalHarc: aksamResult.NormalHarc,
            NormalReddiyat: aksamResult.NormalReddiyat,
            NormalStopaj: aksamResult.NormalStopaj,
            OnlineStopaj: aksamResult.OnlineStopaj,
            ToplamStopaj: aksamResult.ToplamStopaj,
            BankayaYatirilacakHarc: aksamResult.BankayaYatirilacakHarc,
            BankayaYatirilacakNakit: aksamResult.BankayaYatirilacakNakit,
            BankayaYatirilacakStopaj: aksamResult.BankayaYatirilacakStopaj,
            GenelKasa: aksamResult.GenelKasa,
            BankaGoturulecekNakit: aksamResult.BankaGoturulecekNakit,
            BozukParaHaricKasa: aksamResult.BozukParaHaricKasa,
            // Sabah Kasa özgü değerler
            StopajKontrol: stopajKontrol,
            GuneAitEksikFazlaTahsilat: guneAitEksikFazlaTahsilat,
            DundenEksikFazlaTahsilat: dundenEksikFazlaTahsilat,
            OncekiGuneAitEksikFazlaTahsilat: oncekiGuneAitEksikFazlaTahsilat,
            GuneAitEksikFazlaHarc: guneAitEksikFazlaHarc,
            DundenEksikFazlaHarc: dundenEksikFazlaHarc,
            OncekiGuneAitEksikFazlaHarc: oncekiGuneAitEksikFazlaHarc);
    }

    /// <summary>
    /// Sabah Kasa formül açıklamaları üretir (trace/debug amaçlı).
    /// </summary>
    public static Dictionary<string, string> BuildFormulaExplanations(
        SabahKasaInputs inputs,
        SabahKasaResult result)
    {
        static string fmt(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);

        // Önce Akşam formüllerini al ve Sabah'a dönüştür
        var aksamInputs = inputs.AksamInputs;
        var aksamResult = AksamKasaCalculator.Calculate(aksamInputs);
        var aksamExplanations = AksamKasaCalculator.BuildFormulaExplanations(aksamInputs, aksamResult);

        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        // AksamKasaCalculator artık kanonik key'ler üretiyor — doğrudan ekle
        foreach (var kv in aksamExplanations)
        {
            d[kv.Key] = kv.Value.Replace("Akşam", "Sabah", StringComparison.OrdinalIgnoreCase);
        }

        // Sabah Kasa özgü formüller
        d["stopaj_kontrol"] = $"Stopaj Kontrol = 0 (Sabah Kasa sabit)";

        d["gune_ait_eksik_fazla_tahsilat"] = 
            $"Gün içi Ek.Faz T = BankaGiren - ((OnlineTahsilat - OnlineReddiyat) + (ToplamTahsilat - BankayaYatirilacakNakit))\n" +
            $"= {fmt(inputs.BankaGiren)} - (({fmt(inputs.OnlineTahsilat)} - {fmt(aksamInputs.OnlineReddiyat)}) + ({fmt(aksamInputs.UstTahsilat)} - {fmt(result.BankayaYatirilacakNakit)}))\n" +
            $"= {fmt(result.GuneAitEksikFazlaTahsilat)}";

        d["dunden_eksik_fazla_tahsilat"] = 
            $"Dünden Ek.Faz T = Önceki günün Gün içi değeri = {fmt(result.DundenEksikFazlaTahsilat)}";

        d["onceki_gune_ait_eksik_fazla_tahsilat"] = 
            $"Önceki güne ait = Önceki günün (Önceki + Dünden) = {fmt(result.OncekiGuneAitEksikFazlaTahsilat)}";

        d["gune_ait_eksik_fazla_harc"] = 
            $"Gün içi Ek.Faz H = BankaHarcGiren - (OnlineHarc + ToplamHarc)\n" +
            $"= {fmt(inputs.BankaHarcGiren)} - ({fmt(inputs.OnlineHarc)} + {fmt(inputs.ToplamHarc)})\n" +
            $"= {fmt(result.GuneAitEksikFazlaHarc)}";

        d["dunden_eksik_fazla_harc"] = 
            $"Dünden Ek.Faz H = Önceki günün Gün içi değeri = {fmt(result.DundenEksikFazlaHarc)}";

        d["onceki_gune_ait_eksik_fazla_harc"] = 
            $"Önceki güne ait = Önceki günün (Önceki + Dünden) = {fmt(result.OncekiGuneAitEksikFazlaHarc)}";

        return d;
    }
}
