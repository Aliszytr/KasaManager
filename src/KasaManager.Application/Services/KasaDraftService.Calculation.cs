using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Constants;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services;

// Calculation: DetermineDevredenKasaAsync, BuildAksamInlineFormulas, BuildSabahInlineFormulas, CalculateAksamLegacy
public sealed partial class KasaDraftService
{


    private async Task<decimal> DetermineDevredenKasaAsync(DateOnly raporTarihi, List<string> issues, CancellationToken ct)
    {
        // R14C: Öncelik - Ayarlardaki override (0/boş değilse)
        try
        {
            var defaults = await _globalDefaults.GetAsync(ct);
            var overrideVal = defaults.DefaultDundenDevredenKasaNakit;
            if (overrideVal.HasValue && overrideVal.Value != 0m)
            {
                issues.Add($"DEVREDEN OVERRIDE: Ayarlardaki Dünden Devreden Kasa kullanıldı: {overrideVal.Value:N2}.");
                return overrideVal.Value;
            }
        }
        catch (Exception ex)
        {
            issues.Add($"Devreden Kasa: Ayarlardan okuma başarısız. Fallback'e geçiliyor. Hata={ex.Message}");
        }

        // R14C: Fallback - bir önceki gün aynı kasa (Akşam) snapshot'ından GenelKasa/Devreden alanı
        return await TryReadDevredenKasaFromPreviousAksamSnapshotAsync(raporTarihi, issues, ct);
    }

    private async Task<decimal> TryReadDevredenKasaFromPreviousAksamSnapshotAsync(DateOnly raporTarihi, List<string> issues, CancellationToken ct)
    {
        try
        {
            var prev = raporTarihi.AddDays(-1);
            var snap = await _snapshots.GetAsync(prev, KasaRaporTuru.Aksam, ct);
            if (snap?.Results == null || string.IsNullOrWhiteSpace(snap.Results.ValuesJson))
            {
                issues.Add($"{prev:dd.MM.yyyy} tarihli Akşam snapshot sonucu bulunamadı. DevredenKasa 0 kabul edildi.");
                return 0m;
            }

            var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(snap.Results.ValuesJson) ?? new();

            // Sonuç json key'leri finalize fazında netleşecek. Şimdilik bilinen birkaç anahtar deneyelim.
            var candidates = new[]
            {
                "KasaSonDurum.GenelKasa",
                "KasaSonDurum_GenelKasa",
                "GenelKasa",
                "Genel_Kasa",
                "genel_kasa"
            };

            foreach (var k in candidates)
            {
                if (dict.TryGetValue(k, out var v) && TryParseDecimal(v, out var d))
                    return d;
            }

            issues.Add($"{prev:dd.MM.yyyy} Akşam snapshot sonucu okundu ancak GenelKasa alanı bulunamadı. DevredenKasa 0 kabul edildi.");
            return 0m;
        }
        catch (Exception ex)
        {
            issues.Add($"DevredenKasa (önceki Akşam snapshot) okuma hatası: {ex.Message}. DevredenKasa 0 kabul edildi.");
            return 0m;
        }
    }

    
private sealed record AksamLegacyCalc(
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

    [Obsolete("LEGACY: Use FormulaEngine instead. Kept for parity/debugging.")]
    private static Dictionary<string, string> BuildAksamInlineFormulas(
        decimal devredenKasa,
        KasaUstSummary ust,
        OnlineReddiyatAgg online,
        decimal vergiKasa,
        decimal kaydenTahsilat,
        decimal kaydenHarc,
        decimal bankayaYatirilacakHarciDegistir,
        decimal ytTahsilatDegistirAuto,
        decimal ytTahsilatDegistirManual,
        decimal bankayaYatirilacakTahsilatiDegistir,
        decimal kasadaKalacakHedef,
        decimal cesitliNedenlerleBankadanCikamayanTahsilat,
        decimal bankadanCekilen,
        decimal vergiGelenKasa,
        decimal bankayaGonderilmisDeger,
        decimal bozukPara,
        AksamLegacyCalc calc)
    {
        string fmt(decimal v) => v.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);

        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // -------------------------
        // Kaynak / Toplamlar (KasaÜstRapor TOPLAMLAR)
        // -------------------------
        d["AksamKasa.ToplamTahsilat"] = @$"Toplam Tahsilat = KasaÜstRapor.TOPLAMLAR.Tahsilat
= {fmt(ust.Tahsilat)}";

        d["AksamKasa.ToplamHarc"] = @$"Toplam Harç = KasaÜstRapor.TOPLAMLAR.Harç
= {fmt(ust.Harc)}";

        d["AksamKasa.OnlineHarc_KasaUst"] = @$"Online Harç (KasaÜst) = KasaÜstRapor.TOPLAMLAR.Online Harç
= {fmt(ust.OnlineHarc)}";

        d["AksamKasa.ToplamReddiyat"] = @$"Toplam Reddiyat = KasaÜstRapor.TOPLAMLAR.Reddiyat
= {fmt(ust.Reddiyat)}";

        // -------------------------
        // Normal (Fiziki) hesaplar
        // -------------------------
        // R15B KİLİTLİ: NormalTahsilat (fiziki tahsilat) KasaÜstRapor.xlsx'teki "Tahsilat" sütunudur.
        // VergiKasa ile burada türetme yapılmaz (ham veri ham kalır).
        d["AksamKasa.NormalTahsilat"] = @$"Normal Tahsilat = KasaÜstRapor.TOPLAMLAR.Tahsilat
= {fmt(ust.Tahsilat)}";

        d["AksamKasa.NormalHarc"] = @$"Normal Harç = Harç (KasaÜstRapor'dan)
= {fmt(calc.NormalHarc)}";

        d["AksamKasa.NormalReddiyat"] = @$"Normal Reddiyat = max(0, Toplam Reddiyat - Online Reddiyat)
= max(0, {fmt(ust.Reddiyat)} - {fmt(online.OnlineReddiyat)})
= {fmt(calc.NormalReddiyat)}";

        d["AksamKasa.NormalStopaj"] = @$"Normal Stopaj = max(0, Toplam Stopaj - Online Stopaj)
= max(0, {fmt(calc.ToplamStopaj)} - {fmt(online.OnlineStopaj)})
= {fmt(calc.NormalStopaj)}";

        d["AksamKasa.ToplamStopaj"] = @$"Toplam Stopaj = KasaÜstRapor.TOPLAMLAR.Stopaj (yoksa Gelir+Damga)
= {fmt(calc.ToplamStopaj)}";

        // -------------------------
        // Değiştir (Delta) alanları
        // -------------------------
        d["AksamKasa.BankayaYatirilacakTahsilatiDegistir"] = @$"Yt.Tahs. Değiştir (Effective) = Auto + Manuel
Auto (hedef): {fmt(ytTahsilatDegistirAuto)}
Manuel (+/-): {fmt(ytTahsilatDegistirManual)}
= {fmt(bankayaYatirilacakTahsilatiDegistir)}

Auto hesabı (KasadaKalacakHedef girildiyse):
BaseForTarget = DevredenKasa + BankadanCekilen + NormalStopaj + VergidenGelen + CNBCıkamayan - KaydenTahsilat
= {fmt(devredenKasa)} + {fmt(bankadanCekilen)} + {fmt(calc.NormalStopaj)} + {fmt(vergiGelenKasa)} + {fmt(cesitliNedenlerleBankadanCikamayanTahsilat)} - {fmt(kaydenTahsilat)}
KasadaKalacakHedef = {fmt(kasadaKalacakHedef)}
Auto = BaseForTarget - Hedef";

        d["AksamKasa.BankayaYatirilacakHarciDegistir"] = @$"Yt.Harç Değiştir (+/-) = kullanıcı girişi
= {fmt(bankayaYatirilacakHarciDegistir)}";

        // -------------------------
        // Bankaya Yatırılacak (IBAN) kalemleri
        // -------------------------
        d["AksamKasa.BankayaYatirilacakHarc"] = @$"Bankaya Yatırılacak Harç = Normal Harç + (Harç Değiştir) - Kayden Harç
= {fmt(calc.NormalHarc)} + ({fmt(bankayaYatirilacakHarciDegistir)}) - {fmt(kaydenHarc)}
= {fmt(calc.BankayaYatirilacakHarc)}";

        var baseMasraf = ust.Tahsilat - calc.NormalReddiyat;
        if (baseMasraf < 0m) baseMasraf = 0m;

        d["AksamKasa.BankayaYatirilacakNakit"] = @$"Bankaya Yatırılacak Nakit = max(0, Toplam Tahsilat - Normal Reddiyat)
+ (Yt.Tahs. Değiştir)
- (VergiKasa + Kayden Tahsilat)
= {fmt(baseMasraf)} + ({fmt(bankayaYatirilacakTahsilatiDegistir)}) - ({fmt(vergiKasa)} + {fmt(kaydenTahsilat)})
= {fmt(calc.BankayaYatirilacakNakit)}";

        d["AksamKasa.BankayaYatirilacakStopaj"] = @$"Bankaya Yatırılacak Stopaj = Normal Stopaj
= {fmt(calc.NormalStopaj)}";

        d["AksamKasa.BankayaYatirilacakToplamMiktar"] = @$"Bankaya Yatırılacak Toplam (Net) = Harç + Nakit + Normal Stopaj - Bankaya Gönderilmiş Değer
= {fmt(calc.BankayaYatirilacakHarc)} + {fmt(calc.BankayaYatirilacakNakit)} + {fmt(calc.BankayaYatirilacakStopaj)} - {fmt(bankayaGonderilmisDeger)}
= {fmt(calc.BankaGoturulecekNakit)}";

        // -------------------------
        // Genel Kasa & Kasadaki Nakit
        // -------------------------
        d["AksamKasa.GenelKasa"] = @$"Genel Kasa = DevredenKasa + BankadanCekilen + VergidenGelen + CNBCıkamayan + NormalTahsilat + NormalStopaj
- (NormalReddiyat + BankayaYatırılacakNakit + Kayden Tahsilat)
= {fmt(devredenKasa)} + {fmt(bankadanCekilen)} + {fmt(vergiGelenKasa)} + {fmt(cesitliNedenlerleBankadanCikamayanTahsilat)} + {fmt(calc.NormalTahsilat)} + {fmt(calc.NormalStopaj)}
- ({fmt(calc.NormalReddiyat)} + {fmt(calc.BankayaYatirilacakNakit)} + {fmt(kaydenTahsilat)})
= {fmt(calc.GenelKasa)}";

        d["AksamKasa.BozukParaHaricKasa"] = @$"Bozuk Para Hariç Kasa = Genel Kasa - Bozuk Para
= {fmt(calc.GenelKasa)} - {fmt(bozukPara)}
= {fmt(calc.BozukParaHaricKasa)}";

        d["AksamKasa.BankaGoturulecekNakit"] = @$"Banka Götürülecek Nakit = Bankaya Yatırılacak Toplam (Net)
= {fmt(calc.BankaGoturulecekNakit)}";

        d["AksamKasa.StopajKontrol"] = @$"Stopaj Kontrol = BankadanCikan - OnlineReddiyat
= {fmt(calc.StopajKontrol)}";

        return d;
    }

    [Obsolete("LEGACY: Use FormulaEngine instead. Kept for parity/debugging.")]
    private static Dictionary<string, string> BuildSabahInlineFormulas(
        decimal devredenKasa,
        KasaUstSummary ust,
        OnlineReddiyatAgg online,
        decimal vergiKasa,
        decimal kaydenTahsilat,
        decimal kaydenHarc,
        decimal bankayaYatirilacakHarciDegistir,
        decimal ytTahsilatDegistirAuto,
        decimal ytTahsilatDegistirManual,
        decimal bankayaYatirilacakTahsilatiDegistir,
        decimal kasadaKalacakHedef,
        decimal cesitliNedenlerleBankadanCikamayanTahsilat,
        decimal bankadanCekilen,
        decimal vergiGelenKasa,
        decimal bankayaGonderilmisDeger,
        decimal bozukPara,
        AksamLegacyCalc calc)
    {
        // Akşam inline formülleriyle aynı biçimde formatla.
        string fmt(decimal v) => v.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);

        var baseDict = BuildAksamInlineFormulas(
            devredenKasa,
            ust,
            online,
            vergiKasa,
            kaydenTahsilat,
            kaydenHarc,
            bankayaYatirilacakHarciDegistir,
            ytTahsilatDegistirAuto,
            ytTahsilatDegistirManual,
            bankayaYatirilacakTahsilatiDegistir,
            kasadaKalacakHedef,
            cesitliNedenlerleBankadanCikamayanTahsilat,
            bankadanCekilen,
            vergiGelenKasa,
            bankayaGonderilmisDeger,
            bozukPara,
            calc);

        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in baseDict)
        {
            var key = kv.Key.StartsWith("AksamKasa.", StringComparison.OrdinalIgnoreCase)
                ? "SabahKasa." + kv.Key.Substring("AksamKasa.".Length)
                : kv.Key;
            d[key] = kv.Value.Replace("AksamKasa", "SabahKasa", StringComparison.OrdinalIgnoreCase);
        }

        // KİLİTLİ R14E2: Sabah Kasa StopajKontrol sabittir.
        d["SabahKasa.StopajKontrol"] = @$"Stopaj Kontrol = 0 (Sabah Kasa sabit)\n= {fmt(0m)}";

        // R14G: Sabah Kasa – Eksik/Fazla taşıma ve parity formülleri
d["SabahKasa.GuneAitEksikYadaFazlaTahsilat"] =
    "Excel parity:\n" +
    "Gün içi Ek.Faz T = BankayaGiren - ( (OnlineTahsilat - OnlineReddiyat) + (ToplamTahsilat - BankayaYatirilacakNakit) )\n" +
    "Not: Bu değer (+/-) olabilir; bankaya yansıma gecikmesi / fazlalık durumunu temsil eder.";

d["SabahKasa.DundenEksikYadaFazlaTahsilat"] =
    "Taşıma kuralı:\n" +
    "Dünden Ek.Faz T = (Bir önceki günün) Gün içi Ek.Faz T\n" +
    "Bugünkü hesaplara dahil edilerek (yansıdıysa) otomatik dengeleme sağlar.";

d["SabahKasa.OncekiGuneAitEksikYadaFazlaTahsilat"] =
    "Taşıma kuralı:\n" +
    "Önceki güne ait Ek.Faz T = (Bir önceki günün) Önceki + Dünden\n" +
    "Detay bilgi korunur; toplam etki bugünde tek bir değer gibi davranır.";

d["SabahKasa.GuneAitEksikYadaFazlaHarc"] =
    "Excel parity:\n" +
    "Gün içi Ek.Faz H = BankaHarcGiren - (OnlineHarc_Dosya + ToplamHarc)";

d["SabahKasa.DundenEksikYadaFazlaHarc"] =
    "Taşıma kuralı:\n" +
    "Dünden Ek.Faz H = (Bir önceki günün) Gün içi Ek.Faz H";

d["SabahKasa.OncekiGuneAitEksikYadaFazlaHarc"] =
    "Taşıma kuralı:\n" +
    "Önceki güne ait Ek.Faz H = (Bir önceki günün) Önceki + Dünden";
return d;
    }


[Obsolete("LEGACY: Use FormulaEngine instead. This method is kept for parity checking only.")]
private static AksamLegacyCalc CalculateAksamLegacy(
    decimal devredenKasa,
    bool isSabah,
    BankaGunAgg bankaTahsilatGun,
    BankaGunAgg bankaHarcGun,
    KasaUstSummary ust,
    OnlineReddiyatAgg online,
    decimal bankayaYatirilacakHarciDegistir,
    decimal bankayaYatirilacakTahsilatiDegistir,
    decimal kaydenTahsilat,
    decimal kaydenHarc,
    decimal vergiKasa,
    decimal vergiGelenKasa,
    decimal bankadanCekilen,
    decimal cesitliNedenlerleBankadanCikamayanTahsilat,
    decimal bankayaGonderilmisDeger,
    decimal bozukPara)
{
    // === Excel parity (KASA RAPORU.xlsx / Kasa_Raporu_Yeni) ===
    // Amaç: Akşam Kasa alanları, referans Excel formülleriyle birebir aynı mantıkla üretilecek.
    //
    // Notlar:
    // - "Normal" = Fiziki (Online hariç)
    // - BankayaYatirilacakStopaj = NormalStopaj (ToplamStopaj - OnlineStopaj, negatifse 0)
    // - NormalStopaj = ToplamStopaj - OnlineStopaj (negatifse 0)
    // - BankayaYatirilacakNakit (Masraf hesabına yatacak) Excel'de:
    //     max(0, Tahsilat - NormalReddiyat) + (± BankayaYatirilacakTahsilatiDegistir)
    //       - (VergiKasa + KaydenTahsilat)
    // - GenelKasa Excel'de:
    //     (DevredenKasa + (BankadanCekilen+VergidenGelen) + NormalTahsilat + NormalStopaj)
    //       - (NormalReddiyat + BankayaYatirilacakNakit + KaydenTahsilat)
    //   (VergiKasa, NormalTahsilat ve BankayaYatirilacakNakit içinde zaten taşındığı için ayrıca çıkarılmaz.)

    // R15B KİLİTLİ: Fiziki (Normal) Tahsilat = KasaÜstRapor.TOPLAMLAR.Tahsilat.
    // Ham katmanda "Tahsilat - VergiKasa" türetmesi yapılmaz.
    var normalTahsilat = ust.Tahsilat;
    if (normalTahsilat < 0m) normalTahsilat = 0m;

    // Fiziki (Normal) Harç (KİLİTLİ R14E2): KasaÜstRapor'daki "Harç" sütunu zaten Normal Harçtır.
    // Bu yüzden "Toplam Harç - Online Harç" gibi bir türetme YOK.
    var normalHarc = ust.Harc;

// Fiziki (Normal) Reddiyat
    var normalReddiyat = ust.Reddiyat - online.OnlineReddiyat;
    if (normalReddiyat < 0m) normalReddiyat = 0m;

    // Stopaj (Gelir+Damga) toplamı ve kırılımı
    var toplamStopaj = ust.Stopaj;
    var onlineStopaj = online.OnlineStopaj;
    var normalStopaj = toplamStopaj - onlineStopaj;
    if (normalStopaj < 0m) normalStopaj = 0m;

    // Excel: BankayaYatirilacakStopaj (sadece NORMAL stopaj)
    var bankayaYatirilacakStopaj = normalStopaj;

    // Excel: BankayaYatirilacakHarc (fiziki harç + manuel düzeltme)
    var bankayaYatirilacakHarc = normalHarc + bankayaYatirilacakHarciDegistir - kaydenHarc;
    if (bankayaYatirilacakHarc < 0m) bankayaYatirilacakHarc = 0m;

    // Excel: BankayaYatirilacakNakit (masraf hesabına yatacak)
    var baseMasraf = ust.Tahsilat - normalReddiyat; // Tahsilat - (ToplamReddiyat - OnlineReddiyat)
    if (baseMasraf < 0m) baseMasraf = 0m;

    var bankayaYatirilacakNakit =
        baseMasraf
        + bankayaYatirilacakTahsilatiDegistir
        - (vergiKasa + kaydenTahsilat);

    if (bankayaYatirilacakNakit < 0m) bankayaYatirilacakNakit = 0m;

    // StopajKontrol (legacy kontrol alanı) – Excel'deki "Online Stopaj Kontrol" ile uyumlu amaç
    var stopajKontrol = isSabah
        ? 0m
        : (bankaTahsilatGun.Cikan - online.OnlineReddiyat);

    // Excel: Genel Kasa
    var genelKasa =
        (devredenKasa + (bankadanCekilen + vergiGelenKasa) + normalTahsilat + normalStopaj)
        + cesitliNedenlerleBankadanCikamayanTahsilat
        - (normalReddiyat + bankayaYatirilacakNakit + kaydenTahsilat);

    // Excel: Bankaya götürülecek nakit = (Harç+Masraf) - (EFT vs)
    var bankaGoturulecekNakit = Math.Max(0m,
        (bankayaYatirilacakHarc + bankayaYatirilacakNakit + bankayaYatirilacakStopaj)
        - bankayaGonderilmisDeger);

    // Excel: Bozuk para hariç kasa
    var bozukParaHaricKasa = genelKasa - bozukPara;

    return new AksamLegacyCalc(
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
}
