using System.Globalization;
using System.Text;

namespace KasaManager.Web.Models;

/// <summary>
/// R4: Legacy Draft alan adlarını ve Engine key'lerini aynı karşılaştırma "canonical" formatına getirir.
/// Hedef: Önce ölç, sonra doğru diffmap ile genişlet.
///
/// Canonical format: lower_snake_case
/// - "SabahKasa.BankayaYatirilacakTahsilat" -> "bankaya_yatirilacak_tahsilat"
/// - "ToplamTahsilat" -> "toplam_tahsilat"
/// - "online_harc" zaten canonical ise aynen kalır.
/// </summary>
public static class ParityKeyNormalizer
{
    public static string NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        var k = key.Trim();

        // Prefix temizle: "SabahKasa." / "AksamKasa." / "GenelKasa." gibi
        var lastDot = k.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < k.Length - 1)
            k = k[(lastDot + 1)..];

        // DiffMap calibration aliases (contract-first)
        // bankadan_cekilen canonical, b_cekilen alias
        if (k.Equals("b_cekilen", StringComparison.OrdinalIgnoreCase))
            return "bankadan_cekilen";

        // Akşam Banka Harç (legacy) -> engine canonical
        if (k.Equals("b_cikan", StringComparison.OrdinalIgnoreCase))
            return "bankadan_cikan_harc";
        if (k.Equals("b_gelen", StringComparison.OrdinalIgnoreCase))
            return "bankaya_giren_harc";
        if (k.Equals("b_devreden", StringComparison.OrdinalIgnoreCase))
            return "banka_devreden_harc";
        if (k.Equals("y_dev_banka", StringComparison.OrdinalIgnoreCase))
            return "banka_yarina_devredecek_harc";

        // Akşam Banka Tahsilat (legacy) -> engine canonical
        if (k.Equals("dunden_devreden_banka", StringComparison.OrdinalIgnoreCase))
            return "banka_devreden_tahsilat";
        if (k.Equals("yarina_deverecek_banka", StringComparison.OrdinalIgnoreCase))
            return "banka_yarina_devredecek_tahsilat";

        // Masraf/Re... legacy ayrımları -> engine canonical
        if (k.Equals("masrafve_reddiyat_masraf", StringComparison.OrdinalIgnoreCase))
            return "masraf";
        if (k.Equals("masrafve_reddiyat_reddiyat", StringComparison.OrdinalIgnoreCase))
            return "masraf_reddiyat";
        if (k.Equals("masrafve_reddiyat_diger", StringComparison.OrdinalIgnoreCase))
            return "masraf_diger";

        // Online Harç (legacy farklı kaynak isimleri) -> engine canonical
        if (k.Equals("online_harc_kasa_ust", StringComparison.OrdinalIgnoreCase))
            return "online_harc";
        if (k.Equals("online_harc_dosya", StringComparison.OrdinalIgnoreCase))
            return "online_harc";

        // Bazı legacy isimleri engine'deki karşılığına yaklaştır
        if (k.Equals("islem_disi_banka_giris_toplam", StringComparison.OrdinalIgnoreCase))
            return "islem_disi_yansiyan";

        // Bazı legacy alanlarda alt çizgi ile kaynak ayrımı var: OnlineHarc_KasaUst gibi
        // Onları da snake_case'e çevirebilmek için '_' üzerinden parçalıyoruz.
        var parts = k.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 1)
        {
            return string.Join('_', parts.Select(ToSnake));
        }

        return ToSnake(k);
    }

    private static string ToSnake(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;

        // Zaten canonical mı?
        var hasUpper = s.Any(char.IsUpper);
        if (!hasUpper && s.Contains('_')) return s.Trim().ToLowerInvariant();

        // Türkçe I/İ normalize (Comparer tarafında sorun çıkmasın)
        s = s.Replace('İ', 'I').Replace('ı', 'i');

        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) continue;

            if (char.IsUpper(c))
            {
                if (i > 0 && sb.Length > 0 && sb[^1] != '_')
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                continue;
            }

            // Diğer karakterler: ayraç gibi davran
            if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }

        // Çoklu '_' temizle
        var normalized = sb.ToString();
        while (normalized.Contains("__", StringComparison.Ordinal))
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        return normalized.Trim('_');
    }


}
