#nullable enable
namespace KasaManager.Domain.FinancialExceptions;

/// <summary>
/// Tür-spesifik BekleyenTutar hesaplama kuralları.
/// Her istisna türü kendi formülünü kullanır — tek paylaşılan formül YOKTUR.
/// </summary>
public static class BekleyenTutarCalculator
{
    /// <summary>
    /// İstisna türüne göre bekleyen (askıdaki) tutarı hesaplar.
    /// Entity-based API — runtime pool projection için kullanılır.
    /// </summary>
    public static decimal Hesapla(FinansalIstisna ex) =>
        Hesapla(ex.Tur, ex.BeklenenTutar, ex.GerceklesenTutar, ex.SistemeGirilenTutar);

    /// <summary>
    /// Tür-spesifik bekleyen tutar hesaplama — raw parametreler ile.
    /// Historical state projection ve runtime projection aynı formülü kullanır.
    /// ARCHITECTURE LOCK: Bu metod tek hesap kaynağıdır. Entity overload da bunu çağırır.
    /// </summary>
    public static decimal Hesapla(IstisnaTuru tur, decimal beklenenTutar, decimal gerceklesenTutar, decimal sistemeGirilenTutar) => tur switch
    {
        // Planlanan virman - gerçekleşen = kasada kalan
        IstisnaTuru.BasarisizVirman => Math.Max(0m, beklenenTutar - gerceklesenTutar),

        // Bankaya gelen EFT - sisteme girilen = girilmeyen kısım
        IstisnaTuru.SistemeGirilmeyenEft => Math.Max(0m, gerceklesenTutar - sistemeGirilenTutar),

        // Beklenen toplam - kısmi giriş = kalan
        IstisnaTuru.KismiIslem => Math.Max(0m, beklenenTutar - sistemeGirilenTutar),

        // BasarisizVirman ile aynı kural
        IstisnaTuru.BankadanCikamayanTutar => Math.Max(0m, beklenenTutar - gerceklesenTutar),

        // Yansıması beklenen ile gerçekleşen fark
        IstisnaTuru.GecikmeliBankaHareketi => Math.Max(0m, beklenenTutar - gerceklesenTutar),

        _ => 0m
    };

    /// <summary>
    /// İstisnanın runtime Pool projection'a katılıp katılmayacağını belirler.
    /// ARCHITECTURE LOCK: Yalnızca Onaylandi + (Acik | KismiCozuldu).
    /// ErtesiGuneDevredildi hiçbir gün hesaplamaya katılmaz.
    /// Faz 2: Tek merkez filtre metodu.
    /// </summary>
    public static bool IsRuntimeEffective(FinansalIstisna ex) =>
        ex.KararDurumu == KararDurumu.Onaylandi
        && ex.Durum is IstisnaDurumu.Acik or IstisnaDurumu.KismiCozuldu;

    /// <summary>Geriye uyumlu alias.</summary>
    public static bool HesaplamayaKatilirMi(FinansalIstisna ex) => IsRuntimeEffective(ex);

    /// <summary>
    /// Canonical pool key üretir: fin_ex_{tur}_{hesapTuru}
    /// </summary>
    public static string CanonicalKey(FinansalIstisna ex)
    {
        var turKey = ex.Tur switch
        {
            IstisnaTuru.BasarisizVirman => "basarisiz_virman",
            IstisnaTuru.SistemeGirilmeyenEft => "sisteme_girilmeyen_eft",
            IstisnaTuru.GecikmeliBankaHareketi => "gecikmeli_banka",
            IstisnaTuru.KismiIslem => "kismi_islem",
            IstisnaTuru.BankadanCikamayanTutar => "bankadan_cikamayan",
            _ => "bilinmeyen"
        };

        var hesapKey = ex.HesapTuru switch
        {
            Reports.HesapKontrol.BankaHesapTuru.Tahsilat => "tahsilat",
            Reports.HesapKontrol.BankaHesapTuru.Harc => "harc",
            Reports.HesapKontrol.BankaHesapTuru.Stopaj => "stopaj",
            _ => "diger"
        };

        return $"fin_ex_{turKey}_{hesapKey}";
    }

    /// <summary>
    /// Aggregate toplam etki key'i.
    /// </summary>
    public const string AggregateTotalKey = "fin_ex_toplam_etki";

    /// <summary>
    /// Explainability: Hesap formülü açıklama metni.
    /// </summary>
    public static string ExplainFormula(FinansalIstisna ex) => ex.Tur switch
    {
        IstisnaTuru.BasarisizVirman =>
            $"BekleyenTutar = BeklenenTutar({ex.BeklenenTutar:N2}) − GerçekleşenTutar({ex.GerceklesenTutar:N2}) = {Hesapla(ex):N2}",
        IstisnaTuru.SistemeGirilmeyenEft =>
            $"BekleyenTutar = GerçekleşenTutar({ex.GerceklesenTutar:N2}) − SistemeGirilenTutar({ex.SistemeGirilenTutar:N2}) = {Hesapla(ex):N2}",
        IstisnaTuru.KismiIslem =>
            $"BekleyenTutar = BeklenenTutar({ex.BeklenenTutar:N2}) − SistemeGirilenTutar({ex.SistemeGirilenTutar:N2}) = {Hesapla(ex):N2}",
        IstisnaTuru.BankadanCikamayanTutar =>
            $"BekleyenTutar = BeklenenTutar({ex.BeklenenTutar:N2}) − GerçekleşenTutar({ex.GerceklesenTutar:N2}) = {Hesapla(ex):N2}",
        IstisnaTuru.GecikmeliBankaHareketi =>
            $"BekleyenTutar = BeklenenTutar({ex.BeklenenTutar:N2}) − GerçekleşenTutar({ex.GerceklesenTutar:N2}) = {Hesapla(ex):N2}",
        _ => "Hesaplama bilgisi mevcut değil"
    };
}
