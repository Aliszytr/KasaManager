using System.Globalization;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Constants;
namespace KasaManager.Application.Services;

// Mapping: BuildSabahFields, BuildAksamFields — Fields dictionary oluşturma
public sealed partial class KasaDraftService
{
    /// <summary>
    /// Sabah Kasa draft'ı için Fields dictionary'si oluşturur.
    /// </summary>
    private static Dictionary<string, string> BuildSabahFields(
        DateOnly raporTarihi,
        List<string> selectedVeznedarlar,

        decimal? bankaBakiye,
        BankaGunAgg bankaGun,
        BankaTahsilatExtraInflowAgg bankaExtra,
        BankaGunAgg bankaHarcGun,
        KasaUstSummary ust,
        OnlineReddiyatAgg online,
        decimal onlineHarc,
        decimal onlineMasraf,
        MasrafReddiyatAgg masrafReddiyat,
        decimal devredenKasa,
        decimal vergiKasa,
        decimal vergiGelenKasa,
        decimal kaydenTahsilat,
        decimal kaydenHarc,
        decimal bankadanCekilen,
        decimal bankayaYatirilacakHarciDegistir,
        decimal bankayaYatirilacakTahsilatiDegistirAuto,
        decimal bankayaYatirilacakTahsilatiDegistirManual,
        decimal bankayaYatirilacakTahsilatiDegistir,
        decimal cesitliNedenlerleBankadanCikamayanTahsilat,
        decimal bankayaGonderilmisDeger,
        decimal bozukPara,
        decimal kasadaKalacakHedef,
        bool negativeKasaGuardActive,
        AksamLegacyCalc sabahCalc,
        EksikFazlaChain efChain,
        KasaDraftFinalizeInputs finalizeInputs)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RaporTarihi"] = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            ["SeciliVeznedarlar"] = selectedVeznedarlar.Count > 0 ? string.Join(", ", selectedVeznedarlar) : "-",
            ["KasaUstRaporSecimToplami"] = "0.00", // P4.4: Snapshot retired
            ["BankaBakiye"] = bankaBakiye.HasValue ? bankaBakiye.Value.ToString("N2", CultureInfo.InvariantCulture) : "-",

            ["BankaTahsilat.BDevreden"] = bankaGun.Devreden.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaTahsilat.YDevBanka"] = bankaGun.Yarina.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaTahsilat.BGelen"] = bankaGun.Giren.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaTahsilat.BCikan"] = bankaGun.Cikan.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaTahsilat.BCekilen"] = 0m.ToString("N2", CultureInfo.InvariantCulture),

            ["BankaHarc.BDevreden"] = bankaHarcGun.Devreden.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaHarc.YDevBanka"] = bankaHarcGun.Yarina.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaHarc.BGelen"] = bankaHarcGun.Giren.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaHarc.BCikan"] = bankaHarcGun.Cikan.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaHarc.BCekilen"] = 0m.ToString("N2", CultureInfo.InvariantCulture),

            ["dunden_devreden_kasa"] = devredenKasa.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.DundenDevredenBankaTahsilat] = bankaGun.Devreden.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.YarinaDeverecekBankaTahsilat] = bankaGun.Yarina.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.BankaGirenTahsilat] = bankaGun.Giren.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.BankaCikanTahsilat] = bankaGun.Cikan.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.EftOtomatikIade] = bankaExtra.EftOtomatikIade.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.GelenHavale] = bankaExtra.GelenHavale.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.IadeKelimesiGiris] = bankaExtra.IadeKelimesiGiris.ToString("N2", CultureInfo.InvariantCulture),
            ["islem_disi_yansiyan"] = bankaExtra.IslemDisiToplam.ToString("N2", CultureInfo.InvariantCulture),

            [KasaCanonicalKeys.DundenDevredenBankaHarc] = bankaHarcGun.Devreden.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.YarinaDeverecekBankaHarc] = bankaHarcGun.Yarina.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.BankaGirenHarc] = bankaHarcGun.Giren.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.BankaCikanHarc] = bankaHarcGun.Cikan.ToString("N2", CultureInfo.InvariantCulture),

            ["pos_tahsilat"] = ust.PosTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["online_tahsilat"] = ust.OnlineTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["post_tahsilat"] = ust.PostTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["gelmeyen_post"] = ust.GelmeyenPost.ToString("N2", CultureInfo.InvariantCulture),
            ["toplam_tahsilat"] = ust.Tahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["normal_tahsilat"] = sabahCalc.NormalTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["negative_kasa_guard"] = negativeKasaGuardActive ? "AKTIF" : "-",

            ["pos_harc"] = ust.PosHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["online_harc"] = ust.OnlineHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["post_harc"] = ust.PostHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["toplam_harc"] = ust.Harc.ToString("N2", CultureInfo.InvariantCulture),
            ["normal_harc"] = sabahCalc.NormalHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["online_harcama"] = onlineHarc.ToString("N2", CultureInfo.InvariantCulture),

            ["toplam_reddiyat"] = ust.Reddiyat.ToString("N2", CultureInfo.InvariantCulture),
            ["online_reddiyat"] = online.OnlineReddiyat.ToString("N2", CultureInfo.InvariantCulture),
            ["online_stopaj"] = online.OnlineStopaj.ToString("N2", CultureInfo.InvariantCulture),
            ["normal_reddiyat"] = sabahCalc.NormalReddiyat.ToString("N2", CultureInfo.InvariantCulture),
            ["normal_stopaj"] = sabahCalc.NormalStopaj.ToString("N2", CultureInfo.InvariantCulture),
            ["toplam_stopaj"] = sabahCalc.ToplamStopaj.ToString("N2", CultureInfo.InvariantCulture),

            ["vergi_kasa_bakiye_toplam"] = vergiKasa.ToString("N2", CultureInfo.InvariantCulture),
            ["vergi_kasa"] = vergiKasa.ToString("N2", CultureInfo.InvariantCulture),
            ["vergiden_gelen"] = vergiGelenKasa.ToString("N2", CultureInfo.InvariantCulture),
            ["kayden_tahsilat"] = kaydenTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["kayden_harc"] = kaydenHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["bankadan_cekilen"] = bankadanCekilen.ToString("N2", CultureInfo.InvariantCulture),

            ["bankaya_yatirilacak_harci_degistir"] = bankayaYatirilacakHarciDegistir.ToString("N2", CultureInfo.InvariantCulture),
            ["kasada_kalacak_hedef"] = kasadaKalacakHedef.ToString("N2", CultureInfo.InvariantCulture),
            ["yt_tahsilat_degistir_auto"] = bankayaYatirilacakTahsilatiDegistirAuto.ToString("N2", CultureInfo.InvariantCulture),
            ["yt_tahsilat_degistir_manuel"] = bankayaYatirilacakTahsilatiDegistirManual.ToString("N2", CultureInfo.InvariantCulture),
            ["bankaya_yatirilacak_tahsilati_degistir"] = bankayaYatirilacakTahsilatiDegistir.ToString("N2", CultureInfo.InvariantCulture),
            ["cesitli_nedenlerle_bankadan_cikamayan_tahsilat"] = cesitliNedenlerleBankadanCikamayanTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["bankaya_gonderilmis_deger"] = bankayaGonderilmisDeger.ToString("N2", CultureInfo.InvariantCulture),

            ["bankaya_yatirilacak_harc"] = sabahCalc.BankayaYatirilacakHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["bankaya_yatirilacak_nakit"] = sabahCalc.BankayaYatirilacakNakit.ToString("N2", CultureInfo.InvariantCulture),
            ["bankaya_yatirilacak_stopaj"] = sabahCalc.BankayaYatirilacakStopaj.ToString("N2", CultureInfo.InvariantCulture),
            ["stopaj_kontrol"] = sabahCalc.StopajKontrol.ToString("N2", CultureInfo.InvariantCulture),

            ["genel_kasa"] = sabahCalc.GenelKasa.ToString("N2", CultureInfo.InvariantCulture),
            ["banka_goturulecek_nakit"] = sabahCalc.BankaGoturulecekNakit.ToString("N2", CultureInfo.InvariantCulture),
            ["bozuk_para"] = bozukPara.ToString("N2", CultureInfo.InvariantCulture),
            ["bozuk_para_haric_kasa"] = sabahCalc.BozukParaHaricKasa.ToString("N2", CultureInfo.InvariantCulture),

            ["LEGACY_genel_kasa"] = sabahCalc.GenelKasa.ToString("N2", CultureInfo.InvariantCulture),
            ["LEGACY_bankaya_yatirilacak_tahsilat"] = sabahCalc.BankayaYatirilacakNakit.ToString("N2", CultureInfo.InvariantCulture),
            ["LEGACY_bankaya_yatirilacak_harc"] = sabahCalc.BankayaYatirilacakHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["LEGACY_bozuk_para_haric_kasa"] = sabahCalc.BozukParaHaricKasa.ToString("N2", CultureInfo.InvariantCulture),

            ["masraf"] = masrafReddiyat.Masraf.ToString("N2", CultureInfo.InvariantCulture),
            ["masraf_reddiyat"] = masrafReddiyat.Reddiyat.ToString("N2", CultureInfo.InvariantCulture),
            ["masraf_diger"] = masrafReddiyat.Diger.ToString("N2", CultureInfo.InvariantCulture),
            ["online_masraf"] = onlineMasraf.ToString("N2", CultureInfo.InvariantCulture),

            ["dunden_eksik_yada_fazla_tahsilat"] = efChain.DundenTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["gune_ait_eksik_yada_fazla_tahsilat"] = efChain.GuneTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["onceki_gune_ait_eksik_yada_fazla_tahsilat"] = efChain.OncekiTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["dunden_eksik_yada_fazla_harc"] = efChain.DundenHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["gune_ait_eksik_yada_fazla_harc"] = efChain.GuneHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["onceki_gune_ait_eksik_yada_fazla_harc"] = efChain.OncekiHarc.ToString("N2", CultureInfo.InvariantCulture),

            ["gelir_vergisi"] = ust.GelirVergisi.ToString("N2", CultureInfo.InvariantCulture),
            ["damga_vergisi"] = ust.DamgaVergisi.ToString("N2", CultureInfo.InvariantCulture),
            ["toplam_stopaj_ust"] = ust.Stopaj.ToString("N2", CultureInfo.InvariantCulture),

            ["Finalize.KasayiYapan"] = finalizeInputs.KasayiYapan ?? "-",
            ["Finalize.Aciklama"] = finalizeInputs.Aciklama ?? "-",
            ["Finalize.BozukPara"] = (finalizeInputs.BozukPara ?? 0m).ToString("N2", CultureInfo.InvariantCulture),
            ["Finalize.NakitPara"] = (finalizeInputs.NakitPara ?? 0m).ToString("N2", CultureInfo.InvariantCulture),
            ["Finalize.VergiKasaBakiyeToplam"] = (finalizeInputs.VergiKasaBakiyeToplam ?? 0m).ToString("N2", CultureInfo.InvariantCulture),
            ["Finalize.VergidenGelen"] = (finalizeInputs.VergidenGelen ?? 0m).ToString("N2", CultureInfo.InvariantCulture),

            ["KasaSonDurum.DevredenKasa"] = devredenKasa.ToString("N2", CultureInfo.InvariantCulture),
            ["KasaSonDurum.GenelKasa"] = sabahCalc.GenelKasa.ToString("N2", CultureInfo.InvariantCulture),
            ["KasaSonDurum.BphKasa"] = sabahCalc.BozukParaHaricKasa.ToString("N2", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Akşam Kasa draft'ı için Fields dictionary'si oluşturur.
    /// </summary>
    private static Dictionary<string, string> BuildAksamFields(
        DateOnly raporTarihi,
        List<string> selectedVeznedarlar,

        decimal? bankaBakiye,
        BankaGunAgg bankaGun,
        BankaTahsilatExtraInflowAgg bankaExtra,
        BankaGunAgg bankaHarcGun,
        KasaUstSummary ust,
        OnlineReddiyatAgg online,
        decimal onlineHarc,
        decimal onlineMasraf,
        MasrafReddiyatAgg masrafReddiyat,
        decimal devredenKasa,
        decimal normalTahsilat,
        decimal vergiKasa,
        decimal vergiGelenKasa,
        decimal kaydenTahsilat,
        decimal kaydenHarc,
        decimal bankadanCekilen,
        decimal bankayaYatirilacakHarciDegistir,
        decimal bankayaYatirilacakTahsilatiDegistirAuto,
        decimal bankayaYatirilacakTahsilatiDegistirManual,
        decimal bankayaYatirilacakTahsilatiDegistir,
        decimal cesitliNedenlerleBankadanCikamayanTahsilat,
        decimal bankayaGonderilmisDeger,
        decimal bozukPara,
        decimal kasadaKalacakHedef,
        bool negativeKasaGuardActive,
        AksamLegacyCalc aksamCalc,
        KasaDraftFinalizeInputs finalizeInputs)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["kasa_tarihi"] = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            ["vergi_bina_kasa"] = vergiKasa.ToString("N2", CultureInfo.InvariantCulture),

            ["RaporTarihi"] = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            ["SeciliVeznedarlar"] = selectedVeznedarlar.Count > 0 ? string.Join(", ", selectedVeznedarlar) : "-",
            ["KasaUstRaporSecimToplami"] = "0.00", // P4.4: Snapshot retired
            ["BankaBakiye"] = bankaBakiye.HasValue ? bankaBakiye.Value.ToString("N2", CultureInfo.InvariantCulture) : "-",

            ["BankaTahsilat.BDevreden"] = bankaGun.Devreden.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaTahsilat.YDevBanka"] = bankaGun.Yarina.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaTahsilat.BGelen"] = bankaGun.Giren.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaTahsilat.BCikan"] = bankaGun.Cikan.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaTahsilat.BCekilen"] = 0m.ToString("N2", CultureInfo.InvariantCulture),

            ["BankaHarc.BDevreden"] = bankaHarcGun.Devreden.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaHarc.YDevBanka"] = bankaHarcGun.Yarina.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaHarc.BGelen"] = bankaHarcGun.Giren.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaHarc.BCikan"] = bankaHarcGun.Cikan.ToString("N2", CultureInfo.InvariantCulture),
            ["BankaHarc.BCekilen"] = 0m.ToString("N2", CultureInfo.InvariantCulture),

            ["dunden_devreden_kasa"] = devredenKasa.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.DundenDevredenBankaTahsilat] = bankaGun.Devreden.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.YarinaDeverecekBankaTahsilat] = bankaGun.Yarina.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.BankaGirenTahsilat] = bankaGun.Giren.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.BankaCikanTahsilat] = bankaGun.Cikan.ToString("N2", CultureInfo.InvariantCulture),

            [KasaCanonicalKeys.EftOtomatikIade] = bankaExtra.EftOtomatikIade.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.GelenHavale] = bankaExtra.GelenHavale.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.IadeKelimesiGiris] = bankaExtra.IadeKelimesiGiris.ToString("N2", CultureInfo.InvariantCulture),
            ["islem_disi_yansiyan"] = bankaExtra.IslemDisiToplam.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.DundenDevredenBankaHarc] = bankaHarcGun.Devreden.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.YarinaDeverecekBankaHarc] = bankaHarcGun.Yarina.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.BankaGirenHarc] = bankaHarcGun.Giren.ToString("N2", CultureInfo.InvariantCulture),
            [KasaCanonicalKeys.BankaCikanHarc] = bankaHarcGun.Cikan.ToString("N2", CultureInfo.InvariantCulture),

            ["pos_tahsilat"] = ust.PosTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["online_tahsilat"] = ust.OnlineTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["post_tahsilat"] = ust.PostTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["gelmeyen_post"] = ust.GelmeyenPost.ToString("N2", CultureInfo.InvariantCulture),
            ["toplam_tahsilat"] = ust.Tahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["normal_tahsilat"] = normalTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["negative_kasa_guard"] = negativeKasaGuardActive ? "AKTIF" : "-",

            ["pos_harc"] = ust.PosHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["online_harc"] = ust.OnlineHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["post_harc"] = ust.PostHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["toplam_harc"] = ust.Harc.ToString("N2", CultureInfo.InvariantCulture),
            ["normal_harc"] = aksamCalc.NormalHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["online_harcama"] = onlineHarc.ToString("N2", CultureInfo.InvariantCulture),

            ["toplam_reddiyat"] = ust.Reddiyat.ToString("N2", CultureInfo.InvariantCulture),
            ["online_reddiyat"] = online.OnlineReddiyat.ToString("N2", CultureInfo.InvariantCulture),
            ["online_stopaj"] = online.OnlineStopaj.ToString("N2", CultureInfo.InvariantCulture),
            ["normal_reddiyat"] = aksamCalc.NormalReddiyat.ToString("N2", CultureInfo.InvariantCulture),
            ["normal_stopaj"] = aksamCalc.NormalStopaj.ToString("N2", CultureInfo.InvariantCulture),
            ["toplam_stopaj"] = aksamCalc.ToplamStopaj.ToString("N2", CultureInfo.InvariantCulture),

            ["vergi_kasa_bakiye_toplam"] = vergiKasa.ToString("N2", CultureInfo.InvariantCulture),
            ["vergi_kasa"] = vergiKasa.ToString("N2", CultureInfo.InvariantCulture),
            ["vergiden_gelen"] = vergiGelenKasa.ToString("N2", CultureInfo.InvariantCulture),
            ["kayden_tahsilat"] = kaydenTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["kayden_harc"] = kaydenHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["bankadan_cekilen"] = bankadanCekilen.ToString("N2", CultureInfo.InvariantCulture),

            ["bankaya_yatirilacak_harci_degistir"] = bankayaYatirilacakHarciDegistir.ToString("N2", CultureInfo.InvariantCulture),
            ["kasada_kalacak_hedef"] = kasadaKalacakHedef.ToString("N2", CultureInfo.InvariantCulture),
            ["yt_tahsilat_degistir_auto"] = bankayaYatirilacakTahsilatiDegistirAuto.ToString("N2", CultureInfo.InvariantCulture),
            ["yt_tahsilat_degistir_manuel"] = bankayaYatirilacakTahsilatiDegistirManual.ToString("N2", CultureInfo.InvariantCulture),
            ["bankaya_yatirilacak_tahsilati_degistir"] = bankayaYatirilacakTahsilatiDegistir.ToString("N2", CultureInfo.InvariantCulture),
            ["cesitli_nedenlerle_bankadan_cikamayan_tahsilat"] = cesitliNedenlerleBankadanCikamayanTahsilat.ToString("N2", CultureInfo.InvariantCulture),
            ["bankaya_gonderilmis_deger"] = bankayaGonderilmisDeger.ToString("N2", CultureInfo.InvariantCulture),

            ["bankaya_yatirilacak_harc"] = aksamCalc.BankayaYatirilacakHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["bankaya_yatirilacak_nakit"] = aksamCalc.BankayaYatirilacakNakit.ToString("N2", CultureInfo.InvariantCulture),
            ["bankaya_yatirilacak_stopaj"] = aksamCalc.BankayaYatirilacakStopaj.ToString("N2", CultureInfo.InvariantCulture),

            ["bankaya_yatirilacak_toplam_miktar"] =
                Math.Max(0m,
                    (aksamCalc.BankayaYatirilacakHarc + aksamCalc.BankayaYatirilacakNakit + aksamCalc.BankayaYatirilacakStopaj)
                    - bankayaGonderilmisDeger)
                .ToString("N2", CultureInfo.InvariantCulture),
            ["stopaj_kontrol"] = aksamCalc.StopajKontrol.ToString("N2", CultureInfo.InvariantCulture),

            ["genel_kasa"] = aksamCalc.GenelKasa.ToString("N2", CultureInfo.InvariantCulture),
            ["banka_goturulecek_nakit"] = aksamCalc.BankaGoturulecekNakit.ToString("N2", CultureInfo.InvariantCulture),
            ["bozuk_para"] = bozukPara.ToString("N2", CultureInfo.InvariantCulture),
            ["bozuk_para_haric_kasa"] = aksamCalc.BozukParaHaricKasa.ToString("N2", CultureInfo.InvariantCulture),

            ["kasadaki_anlik_nakit"] = aksamCalc.GenelKasa.ToString("N2", CultureInfo.InvariantCulture),

            ["LEGACY_genel_kasa"] = aksamCalc.GenelKasa.ToString("N2", CultureInfo.InvariantCulture),
            ["LEGACY_bankaya_yatirilacak_tahsilat"] = aksamCalc.BankayaYatirilacakNakit.ToString("N2", CultureInfo.InvariantCulture),
            ["LEGACY_bankaya_yatirilacak_harc"] = aksamCalc.BankayaYatirilacakHarc.ToString("N2", CultureInfo.InvariantCulture),
            ["LEGACY_bozuk_para_haric_kasa"] = aksamCalc.BozukParaHaricKasa.ToString("N2", CultureInfo.InvariantCulture),

            ["masraf"] = masrafReddiyat.Masraf.ToString("N2", CultureInfo.InvariantCulture),
            ["masraf_reddiyat"] = masrafReddiyat.Reddiyat.ToString("N2", CultureInfo.InvariantCulture),
            ["masraf_diger"] = masrafReddiyat.Diger.ToString("N2", CultureInfo.InvariantCulture),
            ["online_masraf"] = onlineMasraf.ToString("N2", CultureInfo.InvariantCulture),

            ["gelir_vergisi"] = ust.GelirVergisi.ToString("N2", CultureInfo.InvariantCulture),
            ["damga_vergisi"] = ust.DamgaVergisi.ToString("N2", CultureInfo.InvariantCulture),
            ["toplam_stopaj_ust"] = ust.Stopaj.ToString("N2", CultureInfo.InvariantCulture),

            ["Finalize.KasayiYapan"] = finalizeInputs.KasayiYapan ?? "-",
            ["Finalize.Aciklama"] = finalizeInputs.Aciklama ?? "-",
            ["Finalize.BozukPara"] = (finalizeInputs.BozukPara ?? 0m).ToString("N2", CultureInfo.InvariantCulture),
            ["Finalize.NakitPara"] = (finalizeInputs.NakitPara ?? 0m).ToString("N2", CultureInfo.InvariantCulture),

            ["Finalize.VergiKasaBakiyeToplam"] = (finalizeInputs.VergiKasaBakiyeToplam ?? 0m).ToString("N2", CultureInfo.InvariantCulture),
            ["Finalize.VergidenGelen"] = (finalizeInputs.VergidenGelen ?? 0m).ToString("N2", CultureInfo.InvariantCulture),
            ["KasaSonDurum.DevredenKasa"] = devredenKasa.ToString("N2", CultureInfo.InvariantCulture),
            ["KasaSonDurum.GenelKasa"] = aksamCalc.GenelKasa.ToString("N2", CultureInfo.InvariantCulture),
            ["KasaSonDurum.BphKasa"] = aksamCalc.BozukParaHaricKasa.ToString("N2", CultureInfo.InvariantCulture),
        };
    }
}
