#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using System.Globalization;
using System.Text;

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
        return $"{GetFollowIdentityFingerprint(k)}|{k.AnalizTarihi:yyyy-MM-dd}";
    }

    private static string GetFollowIdentityFingerprint(HesapKontrolKaydi k)
    {
        var discriminator = !string.IsNullOrWhiteSpace(k.DosyaNo)
            ? NormalizeFollowText(k.DosyaNo)
            : NormalizeFollowText(k.Aciklama);
        var birim = NormalizeFollowText(k.BirimAdi);
        return string.Join('|',
            k.HesapTuru,
            k.Yon,
            discriminator,
            birim,
            k.Tutar.ToString("F2", CultureInfo.InvariantCulture));
    }

    private static string NormalizeFollowText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var normalized = value.Trim().ToUpperInvariant()
            .Replace('\u0130', 'I')
            .Replace('\u0131', 'I')
            .Replace('\u00dc', 'U')
            .Replace('\u00fc', 'U')
            .Replace('\u00d6', 'O')
            .Replace('\u00f6', 'O')
            .Replace('\u015e', 'S')
            .Replace('\u015f', 'S')
            .Replace('\u00c7', 'C')
            .Replace('\u00e7', 'C')
            .Replace('\u011e', 'G')
            .Replace('\u011f', 'G');

        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Reddiyat raporundan Stopaj virman durumunu kontrol eder.
    /// </summary>
    private static StopajVirmanDurum CheckStopajFromReport(ComparisonReport reddiyatRapor)
    {
        var toplamStopaj = reddiyatRapor.TotalStopaj;
        if (toplamStopaj <= 0)
            return new StopajVirmanDurum(true, 0, null, "Stopaj tutarı yok.", StopajStatus.Ok);

        var virmanlar = reddiyatRapor.SurplusBankaRecords
            .Where(x => x.DetectedType == "VIRMAN")
            .Select(x => Math.Abs(x.Tutar))
            .ToList();

        return CheckStopajFromAllVirmans(toplamStopaj, virmanlar, reddiyatRapor.CancelledPairs);
    }

    /// <summary>
    /// TÜM karşılaştırma kaynaklarındaki VIRMAN tutarlarını kullanarak
    /// Stopaj virman durumunu kontrol eder.
    /// Parçalı virman desteği ve iptal mantığı içerir.
    /// </summary>
    internal static StopajVirmanDurum CheckStopajFromAllVirmans(
        decimal hedefStopajTutari,
        List<decimal> gecerliVirmanTutarlar,
        List<CancelledPair>? iptalKayitlari = null)
    {
        if (hedefStopajTutari <= 0)
            return new StopajVirmanDurum(true, 0, null, "Stopaj tutarı yok.", StopajStatus.Ok);

        iptalKayitlari ??= new List<CancelledPair>();
        
        // Sadece Virman türündeki iptalleri al
        var iptalEdilenVirmanlar = iptalKayitlari
            .Where(r => string.Equals(r.Tur, "VIRMAN", StringComparison.OrdinalIgnoreCase))
            .ToList();

        decimal toplamGecerliVirman = gecerliVirmanTutarlar.Sum();

        StopajStatus status;
        string message;

        if (iptalEdilenVirmanlar.Count > 0 && toplamGecerliVirman > 0 && toplamGecerliVirman >= hedefStopajTutari)
        {
            // Durum: İptal var AMA yerine doğrusu yapılmış
            status = StopajStatus.OkWithNote;
            message = $"ℹ️ {iptalEdilenVirmanlar.Sum(x => x.Tutar):N2} ₺ virman iptal edildi, yerine doğru tutarla yeniden yapıldı.";
        }
        else if (iptalEdilenVirmanlar.Count > 0 && toplamGecerliVirman < hedefStopajTutari)
        {
            // Durum: İptal var, yerine yenisi YAPILMAMIŞ (Senaryo B veya D)
            status = StopajStatus.WarningPending;
            message = toplamGecerliVirman == 0
                ? "⚠️ Tüm virman işlemleri iptal edildi. Stopaj virmanı yapılmamış durumda."
                : $"⚠️ {iptalEdilenVirmanlar.Sum(x => x.Tutar):N2} ₺ virman iptal edildi, ancak yerine yeni virman yapılmamış. Stopaj askıda.";
        }
        else if (iptalEdilenVirmanlar.Count == 0 && toplamGecerliVirman < hedefStopajTutari)
        {
            // Durum: Gerçek eksik — mevcut KIRMIZI uyarı (mevcut davranış korunur)
            status = StopajStatus.Error;
            message = toplamGecerliVirman == 0
                ? $"⚠️ Stopaj Hesabına {hedefStopajTutari:N2} ₺ Virman yapılmadığını görüyorum. Lütfen Hesapları kontrol ediniz."
                : $"❌ Stopaj Hesabına {hedefStopajTutari:N2} ₺ virman bekleniyor, en yakın virman: {toplamGecerliVirman:N2} ₺.";
        }
        else
        {
            // Durum: Normal başarılı
            status = StopajStatus.Ok;
            message = $"✅ Stopaj virmanı başarıyla yapılmış: {toplamGecerliVirman:N2} ₺";
        }

        bool isOk = status is StopajStatus.Ok or StopajStatus.OkWithNote;
        decimal? bulunanVirman = toplamGecerliVirman > 0 ? toplamGecerliVirman : null;

        return new StopajVirmanDurum(isOk, hedefStopajTutari, bulunanVirman, message, status);
    }
}
