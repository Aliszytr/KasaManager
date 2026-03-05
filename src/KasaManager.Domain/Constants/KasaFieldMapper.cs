#nullable enable
using System.Collections.Frozen;

namespace KasaManager.Domain.Constants;

/// <summary>
/// REFACTOR R1: Legacy field name → Canonical key mapper.
/// 
/// Bu sınıf, mevcut model property isimlerini (SabahKasaNesnesi, AksamKasaNesnesi vb.)
/// standart canonical key'lere dönüştürür.
/// 
/// Kullanım:
/// - Veri okurken: Legacy field adı → Canonical key
/// - Veri yazarken: Canonical key → Legacy field adı (tersine lookup)
/// </summary>
public static class KasaFieldMapper
{
    /// <summary>
    /// Legacy property name → Canonical key mapping.
    /// Key: Property adı (case-insensitive için lowercase).
    /// Value: Canonical key from KasaCanonicalKeys.
    /// </summary>
    private static readonly FrozenDictionary<string, string> _legacyToCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // ============ Tarih/Metadata ============
        ["IslemTarihiTahsilatSabahK"] = KasaCanonicalKeys.IslemTarihi,
        ["IslemTarihiTahsilat"] = KasaCanonicalKeys.IslemTarihi,
        ["RaporTarihi"] = KasaCanonicalKeys.RaporTarihi,
        ["KasayiYapanSabahK"] = KasaCanonicalKeys.KasayiYapan,
        ["KasayiYapan"] = KasaCanonicalKeys.KasayiYapan,
        ["AciklamaSabahK"] = KasaCanonicalKeys.Aciklama,
        ["Aciklama"] = KasaCanonicalKeys.Aciklama,
        ["Veznedar"] = KasaCanonicalKeys.Veznedar,

        // ============ Tahsilat ============
        ["NormalTahsilatSabahK"] = KasaCanonicalKeys.NormalTahsilat,
        ["NormalTahsilat"] = KasaCanonicalKeys.NormalTahsilat,
        ["OnlineTahsilatSabahK"] = KasaCanonicalKeys.OnlineTahsilat,
        ["OnlineTahsilat"] = KasaCanonicalKeys.OnlineTahsilat,
        ["PostTahsilat"] = KasaCanonicalKeys.PostTahsilat,
        ["KaydenTahsilatSabahK"] = KasaCanonicalKeys.KaydenTahsilat,
        ["KaydenTahsilat"] = KasaCanonicalKeys.KaydenTahsilat,
        ["GelmeyenPost"] = KasaCanonicalKeys.GelmeyenPost,
        ["Tahsilat"] = KasaCanonicalKeys.ToplamTahsilat,

        // ============ Harç ============
        ["NormalHarcSabahK"] = KasaCanonicalKeys.NormalHarc,
        ["NormalHarc"] = KasaCanonicalKeys.NormalHarc,
        ["OnlineHarcSabahK"] = KasaCanonicalKeys.OnlineHarc,
        ["OnlineHarc"] = KasaCanonicalKeys.OnlineHarc,
        ["PostHarc"] = KasaCanonicalKeys.PostHarc,
        ["KaydenHarcSabahK"] = KasaCanonicalKeys.KaydenHarc,
        ["KaydenHarc"] = KasaCanonicalKeys.KaydenHarc,
        ["Harc"] = KasaCanonicalKeys.ToplamHarc,

        // ============ Stopaj ============
        ["NormalStopajSabahK"] = KasaCanonicalKeys.NormalStopaj,
        ["NormalStopaj"] = KasaCanonicalKeys.NormalStopaj,
        ["OnlineStopajSabahK"] = KasaCanonicalKeys.OnlineStopaj,
        ["OnlineStopaj"] = KasaCanonicalKeys.OnlineStopaj,
        ["ToplamStopajSabahK"] = KasaCanonicalKeys.ToplamStopaj,
        ["ToplamStopaj"] = KasaCanonicalKeys.ToplamStopaj,
        ["StopajKontrolSabahK"] = KasaCanonicalKeys.StopajKontrol,
        ["StopajKontrol"] = KasaCanonicalKeys.StopajKontrol,
        ["Stopaj"] = KasaCanonicalKeys.ToplamStopaj,

        // ============ Reddiyat ============
        ["NormalReddiyatSabahK"] = KasaCanonicalKeys.NormalReddiyat,
        ["NormalReddiyat"] = KasaCanonicalKeys.NormalReddiyat,
        ["OnlineReddiyatSabahK"] = KasaCanonicalKeys.OnlineReddiyat,
        ["OnlineReddiyat"] = KasaCanonicalKeys.OnlineReddiyat,
        ["ToplamReddiyatSabahK"] = KasaCanonicalKeys.ToplamReddiyat,
        ["ToplamReddiyat"] = KasaCanonicalKeys.ToplamReddiyat,
        ["Reddiyat"] = KasaCanonicalKeys.ToplamReddiyat,

        // ============ Masraf ============
        ["OnlineMasraf"] = KasaCanonicalKeys.OnlineMasraf,

        // ============ Banka - Tahsilat Hesabı (Sabah Kasa) ============
        ["DundenDevredenBankaTahsilatSabahK"] = KasaCanonicalKeys.DundenDevredenBankaTahsilat,
        ["YarinaDeverecekBankaTahsilatSabahK"] = KasaCanonicalKeys.YarinaDeverecekBankaTahsilat,
        ["BankaGirenTahsilatSabahK"] = KasaCanonicalKeys.BankaGirenTahsilat,
        ["BankaCikanTahsilatSabahK"] = KasaCanonicalKeys.BankaCikanTahsilat,
        ["BankaCekilenTahsilatSabahK"] = KasaCanonicalKeys.BankaCekilenTahsilat,

        // ============ Banka - Harç Hesabı (Sabah Kasa) ============
        ["DundenDevredenBankaHarcSabahK"] = KasaCanonicalKeys.DundenDevredenBankaHarc,
        ["YarinaDeverecekBankaHarcSabahK"] = KasaCanonicalKeys.YarinaDeverecekBankaHarc,
        ["BankaGirenHarcSabahK"] = KasaCanonicalKeys.BankaGirenHarc,
        ["BankaCikanHarcSabahK"] = KasaCanonicalKeys.BankaCikanHarc,
        ["BankaCekilenHarcSabahK"] = KasaCanonicalKeys.BankaCekilenHarc,

        // ============ Banka - Genel (Aksam Kasa) ============
        ["DundenDevredenBanka"] = KasaCanonicalKeys.DundenDevredenBanka,
        ["YarinaDeverecekBanka"] = KasaCanonicalKeys.YarinaDeverecekBanka,
        ["BankayaGiren"] = KasaCanonicalKeys.BankayaGiren,
        ["BankadanCikan"] = KasaCanonicalKeys.BankadanCikan,
        ["BankadanCekilen"] = KasaCanonicalKeys.BankadanCekilen,
        ["Bakiye"] = KasaCanonicalKeys.BankaBakiye,

        // ============ Eksik/Fazla - Tahsilat ============
        ["GelmeyenTahsilatSabahK"] = KasaCanonicalKeys.GelmeyenTahsilat,
        ["DundenEksikFazlaTahsilatSabahK"] = KasaCanonicalKeys.DundenEksikFazlaTahsilat,
        ["DundenEksikYadaFazlaTahsilat"] = KasaCanonicalKeys.DundenEksikFazlaTahsilat, // Alias
        ["GuneAitEksikFazlaTahsilatSabahK"] = KasaCanonicalKeys.GuneAitEksikFazlaTahsilat,
        ["GuneAitEksikYadaFazlaTahsilat"] = KasaCanonicalKeys.GuneAitEksikFazlaTahsilat, // Alias
        ["DundenEksikFazlaGelenTahsilatSabahK"] = KasaCanonicalKeys.DundenEksikFazlaGelenTahsilat,
        ["OncekiGuneAitEksikYadaFazlaTahsilat"] = KasaCanonicalKeys.DundenEksikFazlaGelenTahsilat, // Alias
        ["DuneAitGelenEksikFazlaTahsilatSabahK"] = KasaCanonicalKeys.DuneAitGelenEksikFazlaTahsilat,

        // ============ Eksik/Fazla - Harç ============
        ["GelmeyenHarcSabahK"] = KasaCanonicalKeys.GelmeyenHarc,
        ["DundenEksikFazlaHarcSabahK"] = KasaCanonicalKeys.DundenEksikFazlaHarc,
        ["DundenEksikYadaFazlaHarc"] = KasaCanonicalKeys.DundenEksikFazlaHarc, // Alias
        ["GuneAitEksikFazlaHarcSabahK"] = KasaCanonicalKeys.GuneAitEksikFazlaHarc,
        ["GuneAitEksikYadaFazlaHarc"] = KasaCanonicalKeys.GuneAitEksikFazlaHarc, // Alias
        ["DundenEksikFazlaGelenHarcSabahK"] = KasaCanonicalKeys.DundenEksikFazlaGelenHarc,
        ["OncekiGuneAitEksikYadaFazlaHarc"] = KasaCanonicalKeys.DundenEksikFazlaGelenHarc, // Alias
        ["DuneAitGelenEksikFazlaHarcSabahK"] = KasaCanonicalKeys.DuneAitGelenEksikFazlaHarc,

        // ============ Kasa ============
        ["DundenDevredenKasaSabahK"] = KasaCanonicalKeys.DundenDevredenKasa,
        ["DundenDevredenKasa"] = KasaCanonicalKeys.DundenDevredenKasa,
        ["GenelKasaSabahK"] = KasaCanonicalKeys.GenelKasa,
        ["GenelKasa"] = KasaCanonicalKeys.GenelKasa,
        ["GenelKasaArtiEksiSabahK"] = KasaCanonicalKeys.GenelKasaArtiEksi,
        ["BozukParaHaricKasaSabahK"] = KasaCanonicalKeys.BozukParaHaricKasa,
        ["BozukParaHaricKasa"] = KasaCanonicalKeys.BozukParaHaricKasa,
        ["BozukPara"] = KasaCanonicalKeys.BozukPara,
        ["OncekiGunDevirKasaSabahK"] = KasaCanonicalKeys.OncekiGunDevirKasa,

        // ============ Vergi ============
        ["VergiKasaSabahK"] = KasaCanonicalKeys.VergiKasa,
        ["VergiKasa"] = KasaCanonicalKeys.VergiKasa,
        ["VergiGelenKasaSabahK"] = KasaCanonicalKeys.VergiGelenKasa,
        ["VergiGelenKasa"] = KasaCanonicalKeys.VergiGelenKasa,
        ["GelirVergisi"] = KasaCanonicalKeys.GelirVergisi,
        ["DamgaVergisi"] = KasaCanonicalKeys.DamgaVergisi,

        // ============ Banka Yatırım/Çıkamayan ============
        ["BankayaYatirilacakNakitSabahK"] = KasaCanonicalKeys.BankayaYatirilacakNakit,
        ["BankayaYatirilacakNakit"] = KasaCanonicalKeys.BankayaYatirilacakNakit,
        ["BankayaYatirilacakStopajSabahK"] = KasaCanonicalKeys.BankayaYatirilacakStopaj,
        ["BankayaYatirilacakStopaj"] = KasaCanonicalKeys.BankayaYatirilacakStopaj,
        ["BankayaYatirilacakHarcSabahK"] = KasaCanonicalKeys.BankayaYatirilacakHarc,
        ["BankayaYatirilacakHarc"] = KasaCanonicalKeys.BankayaYatirilacakHarc,
        ["BankayaYatirilacakHarciDegistirSabahK"] = KasaCanonicalKeys.BankayaYatirilacakHarciDegistir,
        ["BankayaYatirilacakHarciDegistir"] = KasaCanonicalKeys.BankayaYatirilacakHarciDegistir,
        ["BankayaYatirilacakTahsilatiDegistirSabahK"] = KasaCanonicalKeys.BankayaYatirilacakTahsilatiDegistir,
        ["BankayaYatirilacakTahsilatiDegistir"] = KasaCanonicalKeys.BankayaYatirilacakTahsilatiDegistir,
        ["CNBankadanCikamayanTahsilatSabahK"] = KasaCanonicalKeys.CNBankadanCikamayanTahsilat,
        ["CesitliNedenlerleBankadanCikamayanTahsilat"] = KasaCanonicalKeys.CNBankadanCikamayanTahsilat,
        ["CNBankadanCikamayanHarcSabahK"] = KasaCanonicalKeys.CNBankadanCikamayanHarc,
        ["CesitliNedenlerleBankadanCikamayanHarc"] = KasaCanonicalKeys.CNBankadanCikamayanHarc,
        ["BankayaGonderilmişDeger"] = KasaCanonicalKeys.BankayaGonderilmisDeger,
        ["BankayaGonderilmisDeger"] = KasaCanonicalKeys.BankayaGonderilmisDeger,
        ["BankaGoturulecekNakit"] = KasaCanonicalKeys.BankaGoturulecekNakit,
        ["GelenEFTIadeSabahK"] = KasaCanonicalKeys.GelenEFTIade,
        ["BToplamYATANSabahK"] = KasaCanonicalKeys.BToplamYATAN,

        // ============ UYAP ============
        ["UyapBakiyeSabahK"] = KasaCanonicalKeys.UyapBakiye,
        ["UyapBakiye"] = KasaCanonicalKeys.UyapBakiye,

        // ============ İşlem Sayıları ============
        ["TahsilatIslemSayisi"] = KasaCanonicalKeys.TahsilatIslemSayisi,
        ["ReddiyatIslemSayisi"] = KasaCanonicalKeys.ReddiyatIslemSayisi,
        ["HarcIslemSayisi"] = KasaCanonicalKeys.HarcIslemSayisi,

    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Legacy property name'den canonical key'e dönüştür.
    /// </summary>
    /// <param name="legacyFieldName">Mevcut model property adı (örn: "NormalTahsilatSabahK")</param>
    /// <returns>Canonical key veya bulunamazsa orijinal isim (lowercase)</returns>
    public static string ToCanonical(string legacyFieldName)
    {
        if (string.IsNullOrWhiteSpace(legacyFieldName))
            return string.Empty;

        return _legacyToCanonical.TryGetValue(legacyFieldName, out var canonical)
            ? canonical
            : legacyFieldName.ToLowerInvariant();
    }

    /// <summary>
    /// Canonical key'in tanımlı olup olmadığını kontrol et.
    /// </summary>
    public static bool IsKnownLegacyField(string legacyFieldName)
    {
        return _legacyToCanonical.ContainsKey(legacyFieldName);
    }

    /// <summary>
    /// Tüm bilinen legacy field isimlerini getir.
    /// </summary>
    public static IEnumerable<string> GetAllLegacyFieldNames() 
        => _legacyToCanonical.Keys;

    /// <summary>
    /// Tüm canonical key'leri getir.
    /// </summary>
    public static IEnumerable<string> GetAllCanonicalKeys() 
        => _legacyToCanonical.Values.Distinct();
}
