#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace KasaManager.Application.Services.Draft.Calculators;

/// <summary>
/// R19: Akşam Kasa hesaplama mantığı.
/// KasaDraftService'ten çıkarılan calculator sınıfı.
/// Excel parity formülleri burada merkezi olarak yönetilir.
/// </summary>
public static class AksamKasaCalculator
{
    /// <summary>
    /// Akşam Kasa hesaplama sonuç kaydı.
    /// </summary>
    public sealed record AksamKasaResult(
        decimal NormalTahsilat,
        decimal NormalHarc,
        decimal NormalReddiyat,
        decimal NormalStopaj,
        decimal OnlineStopaj,
        decimal ToplamStopaj,
        decimal BankayaYatirilacakHarc,
        decimal BankayaYatirilacakNakit,
        decimal BankayaYatirilacakStopaj,
        decimal StopajKontrol,
        decimal GenelKasa,
        decimal BankaGoturulecekNakit,
        decimal BozukParaHaricKasa);

    /// <summary>
    /// Akşam Kasa hesaplama girdi parametreleri.
    /// </summary>
    public sealed record AksamKasaInputs(
        decimal DevredenKasa,
        bool IsSabah,
        decimal BankaTahsilatCikan,
        decimal UstTahsilat,
        decimal UstHarc,
        decimal UstReddiyat,
        decimal UstStopaj,
        decimal OnlineReddiyat,
        decimal OnlineStopaj,
        decimal BankayaYatirilacakHarciDegistir,
        decimal BankayaYatirilacakTahsilatiDegistir,
        decimal KaydenTahsilat,
        decimal KaydenHarc,
        decimal VergiKasa,
        decimal VergiGelenKasa,
        decimal BankadanCekilen,
        decimal CesitliNedenlerleBankadanCikamayanTahsilat,
        decimal BankayaGonderilmisDeger,
        decimal BozukPara);

    /// <summary>
    /// Excel parity Akşam Kasa hesaplaması yapar.
    /// </summary>
    public static AksamKasaResult Calculate(AksamKasaInputs inputs)
    {
        // R15B KİLİTLİ: Fiziki (Normal) Tahsilat = KasaÜstRapor.TOPLAMLAR.Tahsilat.
        var normalTahsilat = Math.Max(0m, inputs.UstTahsilat);

        // Fiziki (Normal) Harç (KİLİTLİ R14E2)
        var normalHarc = inputs.UstHarc;

        // Fiziki (Normal) Reddiyat
        var normalReddiyat = Math.Max(0m, inputs.UstReddiyat - inputs.OnlineReddiyat);

        // Stopaj hesaplamaları
        var toplamStopaj = inputs.UstStopaj;
        var onlineStopaj = inputs.OnlineStopaj;
        var normalStopaj = Math.Max(0m, toplamStopaj - onlineStopaj);

        // BankayaYatirilacakStopaj = NormalStopaj
        var bankayaYatirilacakStopaj = normalStopaj;

        // BankayaYatirilacakHarc = fiziki harç + manuel düzeltme - kayden harç
        var bankayaYatirilacakHarc = Math.Max(0m, 
            normalHarc + inputs.BankayaYatirilacakHarciDegistir - inputs.KaydenHarc);

        // BankayaYatirilacakNakit (masraf hesabına yatacak)
        var baseMasraf = Math.Max(0m, inputs.UstTahsilat - normalReddiyat);
        var bankayaYatirilacakNakit = Math.Max(0m,
            baseMasraf
            + inputs.BankayaYatirilacakTahsilatiDegistir
            - (inputs.VergiKasa + inputs.KaydenTahsilat));

        // StopajKontrol
        var stopajKontrol = inputs.IsSabah 
            ? 0m 
            : (inputs.BankaTahsilatCikan - inputs.OnlineReddiyat);

        // Genel Kasa
        var genelKasa =
            (inputs.DevredenKasa 
             + (inputs.BankadanCekilen + inputs.VergiGelenKasa) 
             + normalTahsilat 
             + normalStopaj)
            + inputs.CesitliNedenlerleBankadanCikamayanTahsilat
            - (normalReddiyat + bankayaYatirilacakNakit + inputs.KaydenTahsilat);

        // Banka götürülecek nakit
        var bankaGoturulecekNakit = Math.Max(0m,
            (bankayaYatirilacakHarc + bankayaYatirilacakNakit + bankayaYatirilacakStopaj)
            - inputs.BankayaGonderilmisDeger);

        // Bozuk para hariç kasa
        var bozukParaHaricKasa = genelKasa - inputs.BozukPara;

        return new AksamKasaResult(
            NormalTahsilat: normalTahsilat,
            NormalHarc: normalHarc,
            NormalReddiyat: normalReddiyat,
            NormalStopaj: normalStopaj,
            OnlineStopaj: onlineStopaj,
            ToplamStopaj: toplamStopaj,
            BankayaYatirilacakHarc: bankayaYatirilacakHarc,
            BankayaYatirilacakNakit: bankayaYatirilacakNakit,
            BankayaYatirilacakStopaj: bankayaYatirilacakStopaj,
            StopajKontrol: stopajKontrol,
            GenelKasa: genelKasa,
            BankaGoturulecekNakit: bankaGoturulecekNakit,
            BozukParaHaricKasa: bozukParaHaricKasa);
    }

    /// <summary>
    /// Akşam Kasa formül açıklamaları üretir (trace/debug amaçlı).
    /// </summary>
    public static Dictionary<string, string> BuildFormulaExplanations(
        AksamKasaInputs inputs,
        AksamKasaResult result)
    {
        static string fmt(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);

        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        d["normal_tahsilat"] = $"Normal Tahsilat = max(0, UstTahsilat) = {fmt(result.NormalTahsilat)}";
        d["normal_harc"] = $"Normal Harç = UstHarc = {fmt(result.NormalHarc)}";
        d["normal_reddiyat"] = $"Normal Reddiyat = max(0, {fmt(inputs.UstReddiyat)} - {fmt(inputs.OnlineReddiyat)}) = {fmt(result.NormalReddiyat)}";
        d["normal_stopaj"] = $"Normal Stopaj = max(0, {fmt(result.ToplamStopaj)} - {fmt(result.OnlineStopaj)}) = {fmt(result.NormalStopaj)}";
        d["toplam_stopaj"] = $"Toplam Stopaj = {fmt(result.ToplamStopaj)}";
        
        d["bankaya_yatirilacak_harc"] = $"Bankaya Yatırılacak Harç = max(0, {fmt(result.NormalHarc)} + {fmt(inputs.BankayaYatirilacakHarciDegistir)} - {fmt(inputs.KaydenHarc)}) = {fmt(result.BankayaYatirilacakHarc)}";
        d["bankaya_yatirilacak_nakit"] = $"Bankaya Yatırılacak Nakit = {fmt(result.BankayaYatirilacakNakit)}";
        d["bankaya_yatirilacak_stopaj"] = $"Bankaya Yatırılacak Stopaj = {fmt(result.BankayaYatirilacakStopaj)}";
        
        d["genel_kasa"] = $"Genel Kasa = {fmt(result.GenelKasa)}";
        d["banka_goturulecek_nakit"] = $"Banka Götürülecek Nakit = {fmt(result.BankaGoturulecekNakit)}";
        d["bozuk_para_haric_kasa"] = $"Bozuk Para Hariç Kasa = {fmt(result.GenelKasa)} - {fmt(inputs.BozukPara)} = {fmt(result.BozukParaHaricKasa)}";
        d["stopaj_kontrol"] = $"Stopaj Kontrol = {fmt(result.StopajKontrol)}";

        return d;
    }
}
