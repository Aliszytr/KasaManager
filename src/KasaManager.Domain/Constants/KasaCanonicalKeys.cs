#nullable enable
namespace KasaManager.Domain.Constants;

/// <summary>
/// Canonical key catalog for UnifiedPool + FormulaEngine.
/// 
/// KİLİTLİ PRENSİP:
/// - String dağınıklığına son: controller/service/view içinde "magic string" kullanma.
/// - Aynı anlamdaki değer, aynı canonical key ile üretilir.
/// 
/// REFACTOR R1: Tüm Kasa alanları için genişletilmiş katalog.
/// Sabah/Aksam/Genel kasaların ortak alanları burada standart isimlerle tanımlanır.
/// </summary>
public static class KasaCanonicalKeys
{
    #region Tarih/Metadata
    public const string IslemTarihi = "islem_tarihi";
    public const string RaporTarihi = "rapor_tarihi";
    public const string KasayiYapan = "kasayi_yapan";
    public const string Aciklama = "aciklama";
    public const string Veznedar = "veznedar";
    #endregion

    #region Tahsilat
    public const string NormalTahsilat = "normal_tahsilat";
    public const string OnlineTahsilat = "online_tahsilat";
    public const string PostTahsilat = "post_tahsilat";
    public const string ToplamTahsilat = "toplam_tahsilat";
    public const string KaydenTahsilat = "kayden_tahsilat";
    public const string GelmeyenPost = "gelmeyen_post";
    #endregion

    #region Harç
    public const string NormalHarc = "normal_harc";
    public const string OnlineHarc = "online_harc";
    public const string PostHarc = "post_harc";
    public const string KaydenHarc = "kayden_harc";
    public const string ToplamHarc = "toplam_harc";
    #endregion

    #region Stopaj
    public const string NormalStopaj = "normal_stopaj";
    public const string OnlineStopaj = "online_stopaj";
    public const string ToplamStopaj = "toplam_stopaj";
    public const string StopajKontrol = "stopaj_kontrol";
    #endregion

    #region Reddiyat
    public const string NormalReddiyat = "normal_reddiyat";
    public const string OnlineReddiyat = "online_reddiyat";
    public const string ToplamReddiyat = "toplam_reddiyat";
    #endregion

    #region Masraf
    public const string OnlineMasraf = "online_masraf";
    #endregion

    #region Banka - Tahsilat Hesabı
    public const string DundenDevredenBankaTahsilat = "dunden_devreden_banka_tahsilat";
    public const string YarinaDeverecekBankaTahsilat = "yarina_deverecek_banka_tahsilat";
    public const string BankaGirenTahsilat = "bankaya_giren_tahsilat";
    public const string BankaCikanTahsilat = "banka_cikan_tahsilat";
    public const string BankaCekilenTahsilat = "banka_cekilen_tahsilat";
    #endregion

    #region Banka - Harç Hesabı
    public const string DundenDevredenBankaHarc = "dunden_devreden_banka_harc";
    public const string YarinaDeverecekBankaHarc = "yarina_deverecek_banka_harc";
    public const string BankaGirenHarc = "bankaya_giren_harc";
    public const string BankaCikanHarc = "banka_cikan_harc";
    public const string BankaCekilenHarc = "banka_cekilen_harc";
    #endregion

    #region Banka - Genel
    public const string DundenDevredenBanka = "dunden_devreden_banka";
    public const string YarinaDeverecekBanka = "yarina_deverecek_banka";
    public const string BankayaGiren = "bankaya_giren";
    public const string BankadanCikan = "bankadan_cikan";
    public const string BankadanCekilen = "bankadan_cekilen";
    public const string BankaBakiye = "banka_bakiye";
    #endregion

    #region Banka - Ek Girişler (BankaTahsilat Extra Inflow)
    public const string EftOtomatikIade = "eft_otomatik_iade";
    public const string GelenHavale = "gelen_havale";
    public const string IadeKelimesiGiris = "iade_kelimesi_giris";
    #endregion

    #region Eksik/Fazla - Tahsilat
    public const string GelmeyenTahsilat = "gelmeyen_tahsilat";
    public const string DundenEksikFazlaTahsilat = "dunden_eksik_fazla_tahsilat";
    public const string GuneAitEksikFazlaTahsilat = "gune_ait_eksik_fazla_tahsilat";
    public const string DundenEksikFazlaGelenTahsilat = "dunden_eksik_fazla_gelen_tahsilat";
    public const string DuneAitGelenEksikFazlaTahsilat = "dune_ait_gelen_eksik_fazla_tahsilat";
    #endregion

    #region Eksik/Fazla - Harç
    public const string GelmeyenHarc = "gelmeyen_harc";
    public const string DundenEksikFazlaHarc = "dunden_eksik_fazla_harc";
    public const string GuneAitEksikFazlaHarc = "gune_ait_eksik_fazla_harc";
    public const string DundenEksikFazlaGelenHarc = "dunden_eksik_fazla_gelen_harc";
    public const string DuneAitGelenEksikFazlaHarc = "dune_ait_gelen_eksik_fazla_harc";
    #endregion

    #region Kasa
    public const string DundenDevredenKasa = "dunden_devreden_kasa";
    public const string GenelKasa = "genel_kasa";
    public const string GenelKasaArtiEksi = "genel_kasa_arti_eksi";
    public const string BozukParaHaricKasa = "bozuk_para_haric_kasa";
    public const string BozukPara = "bozuk_para";
    public const string KasaNakit = "kasa_nakit";
    public const string OncekiGunDevirKasa = "onceki_gun_devir_kasa";
    #endregion

    #region Vergi
    public const string VergiKasa = "vergi_kasa";
    public const string VergiGelenKasa = "vergi_gelen_kasa";
    public const string GelirVergisi = "gelir_vergisi";
    public const string DamgaVergisi = "damga_vergisi";
    #endregion

    #region Banka Yatırım/Çıkamayan
    public const string BankayaYatirilacakNakit = "bankaya_yatirilacak_nakit";
    public const string BankayaYatirilacakStopaj = "bankaya_yatirilacak_stopaj";
    public const string BankayaYatirilacakHarc = "bankaya_yatirilacak_harc";
    public const string BankayaYatirilacakHarciDegistir = "bankaya_yatirilacak_harci_degistir";
    public const string BankayaYatirilacakTahsilatiDegistir = "bankaya_yatirilacak_tahsilati_degistir";
    public const string CNBankadanCikamayanTahsilat = "cn_bankadan_cikamayan_tahsilat";
    public const string CNBankadanCikamayanHarc = "cn_bankadan_cikamayan_harc";
    public const string BankayaGonderilmisDeger = "bankaya_gonderilmis_deger";
    public const string BankaGoturulecekNakit = "banka_goturulecek_nakit";
    public const string GelenEFTIade = "gelen_eft_iade";
    public const string BToplamYATAN = "b_toplam_yatan";
    #endregion

    #region UYAP
    public const string UyapBakiye = "uyap_bakiye";
    #endregion

    #region İşlem Sayıları
    public const string TahsilatIslemSayisi = "tahsilat_islem_sayisi";
    public const string ReddiyatIslemSayisi = "reddiyat_islem_sayisi";
    public const string HarcIslemSayisi = "harc_islem_sayisi";
    #endregion

    #region Eski Uyumluluk (Legacy Keys)
    // Eski kodla uyumluluk için korunan key'ler
    public const string Devreden = "devreden";
    public const string GelmeyenD = "gelmeyen_d";
    public const string EksikFazla = "eksik_fazla";
    public const string TahRedFark = "tah_red_fark";
    public const string SonrayaDevredecek = "sonraya_devredecek";
    public const string BeklenenBanka = "beklenen_banka";
    public const string MutabakatFarki = "mutabakat_farki";
    #endregion
}
