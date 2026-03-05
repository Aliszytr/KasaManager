#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// MS2 CQRS-lite: Dahili yardımcılar (Convert, Classify, Fingerprint, Stopaj).
/// </summary>
public sealed partial class BankaHesapKontrolService
{
    // ═════════════════════════════════════════════════════════════
    // Dahili Yardımcılar
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// ComparisonReport'tan HesapKontrolKaydi listesi oluşturur.
    /// </summary>
    private static List<HesapKontrolKaydi> ConvertToKayitlar(
        ComparisonReport rapor,
        BankaHesapTuru hesapTuru,
        DateOnly analizTarihi,
        string karsilastirmaTuru)
    {
        var kayitlar = new List<HesapKontrolKaydi>();

        foreach (var surplus in rapor.SurplusBankaRecords)
        {
            var sinif = ClassifyFazla(surplus.DetectedType);
            kayitlar.Add(new HesapKontrolKaydi
            {
                AnalizTarihi = analizTarihi,
                HesapTuru = hesapTuru,
                Yon = KayitYonu.Fazla,
                Tutar = Math.Abs(surplus.Tutar),
                Aciklama = surplus.Aciklama,
                Sinif = sinif,
                TespitEdilenTip = surplus.DetectedType,
                KarsilastirmaSatirIndex = surplus.RowIndex,
                KarsilastirmaTuru = karsilastirmaTuru
            });
        }

        foreach (var missing in rapor.MissingBankaRecords)
        {
            kayitlar.Add(new HesapKontrolKaydi
            {
                AnalizTarihi = analizTarihi,
                HesapTuru = hesapTuru,
                Yon = KayitYonu.Eksik,
                Tutar = Math.Abs(missing.Miktar),
                DosyaNo = missing.DosyaNo,
                BirimAdi = missing.BirimAdi,
                Sinif = FarkSinifi.Askida,
                TespitEdilenTip = "BEDELI_GELMEMIS",
                KarsilastirmaSatirIndex = missing.RowIndex,
                KarsilastirmaTuru = karsilastirmaTuru
            });
        }

        return kayitlar;
    }

    /// <summary>
    /// Fazla kaydını DetectedType'a göre FarkSinifi olarak sınıflar.
    /// </summary>
    private static FarkSinifi ClassifyFazla(string? detectedType)
    {
        return detectedType switch
        {
            "EFT_OTOMATIK_IADE" => FarkSinifi.Beklenen,
            "GELEN_HAVALE" => FarkSinifi.Beklenen,
            "MEVDUAT_YATIRMA" => FarkSinifi.Beklenen,
            "VIRMAN" => FarkSinifi.Beklenen,
            "MASRAF" => FarkSinifi.Beklenen,
            "HARÇ" => FarkSinifi.Beklenen,
            "PORTAL" => FarkSinifi.Beklenen,
            "PARAM EP" => FarkSinifi.Beklenen,
            "BAROBİRLİK" => FarkSinifi.Beklenen,
            "BILINMEYEN" => FarkSinifi.Bilinmeyen,
            _ => FarkSinifi.Bilinmeyen
        };
    }

    /// <summary>
    /// Bir HesapKontrolKaydi için bileşik parmak izi (fingerprint) üretir.
    /// </summary>
    private static string GetRecordFingerprint(HesapKontrolKaydi k)
    {
        var discriminator = !string.IsNullOrEmpty(k.DosyaNo) ? k.DosyaNo : (k.Aciklama ?? "");
        return $"{k.HesapTuru}|{k.Yon}|{k.Tutar:F2}|{k.KarsilastirmaTuru ?? ""}|{k.TespitEdilenTip ?? ""}|{discriminator}";
    }

    /// <summary>
    /// Reddiyat raporundan Stopaj virman durumunu kontrol eder.
    /// </summary>
    private static StopajVirmanDurum CheckStopajFromReport(ComparisonReport reddiyatRapor)
    {
        var toplamStopaj = reddiyatRapor.TotalStopaj;
        if (toplamStopaj <= 0)
            return new StopajVirmanDurum(true, 0, null, "Stopaj tutarı yok.");

        var virmanlar = reddiyatRapor.SurplusBankaRecords
            .Where(x => x.DetectedType == "VIRMAN")
            .Select(x => Math.Abs(x.Tutar))
            .ToList();

        return CheckStopajFromAllVirmans(toplamStopaj, virmanlar);
    }

    /// <summary>
    /// TÜM karşılaştırma kaynaklarındaki VIRMAN tutarlarını kullanarak
    /// Stopaj virman durumunu kontrol eder.
    /// </summary>
    private static StopajVirmanDurum CheckStopajFromAllVirmans(
        decimal toplamStopaj, List<decimal> virmanTutarlar)
    {
        if (toplamStopaj <= 0)
            return new StopajVirmanDurum(true, 0, null, "Stopaj tutarı yok.");

        if (virmanTutarlar.Count == 0)
            return new StopajVirmanDurum(
                false, toplamStopaj, null,
                $"⚠️ Stopaj Hesabına {toplamStopaj:N2}₺ Virman yapılmadığını görüyorum. " +
                "Lütfen Hesapları kontrol ediniz.");

        var eslesen = virmanTutarlar.FirstOrDefault(v => v == toplamStopaj);
        if (eslesen != 0)
        {
            return new StopajVirmanDurum(
                true, toplamStopaj, eslesen,
                $"✅ Stopaj virmanı yapılmış ({toplamStopaj:N2}₺).");
        }

        var enYakin = virmanTutarlar.MinBy(v => Math.Abs(v - toplamStopaj));
        return new StopajVirmanDurum(
            false, toplamStopaj, enYakin,
            $"⚠️ Stopaj Hesabına {toplamStopaj:N2}₺ Virman bekleniyor, " +
            $"en yakın virman: {enYakin:N2}₺. Lütfen kontrol ediniz.");
    }
}
