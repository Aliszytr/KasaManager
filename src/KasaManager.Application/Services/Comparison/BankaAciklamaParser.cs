#nullable enable
using System.Text.RegularExpressions;

namespace KasaManager.Application.Services.Comparison;

/// <summary>
/// Banka açıklama alanından parse edilmiş veriler.
/// </summary>
public sealed record ParsedBankaAciklama(
    string? Il,
    string? Mahkeme,
    string? EsasNo,
    string? FoundKeyword,
    double Confidence);

/// <summary>
/// Reddiyat eşleştirmesi için gönderen birim bilgileri.
/// Açıklama formatı: "Ankara 5. Vergi Mahkemesi-2024/950 Vergi-Alıcı..."
/// </summary>
public sealed record ParsedReddiyatGonderici(
    string? GonderenMahkeme,   // "Ankara 5. Vergi Mahkemesi"
    string? GonderenEsasNo,    // "2024/950"
    string? AliciMahkeme,      // "Ankara 1. Vergi Dava Dairesi"
    string? AliciEsasNo,       // "2025/441"
    double Confidence);


/// <summary>
/// Banka açıklama alanını parse eden sınıf.
/// Açıklama metninden Alıcı birim (Ankara) bilgilerini çıkarır.
/// 
/// Açıklama formatı: [Gönderen Birim]-[Alıcı Birim]-[Detaylar]...
/// Örnek: "Mersin 2. İdare Mahkemesi-2026/66 İdare-Ankara 22. İdare Mahkemesi-2026/36-İdare Dava Dosyası..."
/// Bizi ilgilendiren: "-Ankara 22. İdare Mahkemesi-2026/36" kısmı
/// </summary>
public sealed class BankaAciklamaParser
{
    // ─────────────────────────────────────────────────────────────
    // Ankara birimini bulmak için anahtar kelimeler
    // Sıralama önemli: Daha spesifik olanlar önce
    // ─────────────────────────────────────────────────────────────
    private static readonly string[] AnkaraKeywords = new[]
    {
        // Özel durumlar (önce kontrol et)
        "Ankara İdare ve Vergi Mahkemeleri Vezne ve Ön Bürosu",
        
        // Portal Tahsilatı varyasyonları (boşluklu/boşluksuz)
        "Portal Tahsilatıdır. Ankara",   // Nokta + boşluk
        "Portal Tahsilatıdır.Ankara",    // Nokta, boşluksuz
        "Portal Tahsilatıdır-Ankara",    // Tire
        "Portal Tahsilatıdır Ankara",    // Sadece boşluk
        "PortalTahsilatıdır.Ankara",     // Bitişik yazım
        
        // Barobirlik varyasyonları
        "BAROBİRLİK-Ankara",
        "BAROBİRLİK Ankara",
        "BAROBİRLİK-",
        
        // Bölge varyasyonları
        "Bölge-Ankara",
        "Bölge Ankara",
        "Bolge-Ankara",
        "Bolge Ankara",
        
        // Genel Ankara pattern'leri (less specific)
        "-Ankara",
        ".Ankara",
        ". Ankara",   // Nokta + boşluk + Ankara
        "#Ankara",
        "/Ankara",
        " Ankara ",   // Boşlukla çevrili
        
        // Masraf/Harç özel durumlar
        "/Masraf",
        "/Harç",
        "/Harc",
        
        // IBAN bazlı tespit (ANK = Ankara)
        "ANK.BOL.IDARE",
        "ANK.BÖL.İDARE",
        "ANK BOL IDARE",
        "ANKARA BOLGE",
        "ANKARA BÖLGE"
    };

    // Esas no pattern: 20XX/NNNNN veya 20XX-NNNNN formatı
    private static readonly Regex EsasNoPattern = new(
        @"(?<yil>20\d{2})[/\-](?<no>\d{1,6})",
        RegexOptions.Compiled);

    // Mahkeme pattern: "N. İdare/Vergi/İcra Mahkemesi" formatları
    private static readonly Regex MahkemePattern = new(
        @"(\d{1,2})\s*\.?\s*(İdare|Vergi|İcra|Asliye|Hukuk|Ceza|Sulh|Ticaret|İş|Kadastro|Tüketici)\s*(Mahkemesi|Dairesi)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Alternatif mahkeme pattern: "İdare Mahkemesi" (numarasız)
    private static readonly Regex MahkemeAltPattern = new(
        @"(İdare|Vergi|İcra|Asliye|Hukuk|Ceza)\s*(Mahkemesi|Dairesi)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ─────────────────────────────────────────────────────────────
    // Reddiyat açıklama pattern: "İl N. Tip Mahkemesi/Dava Dairesi-YYYY/NNN"
    // Örnekler: 
    //   "Ankara 5. Vergi Mahkemesi-2024/950 Vergi-..."
    //   "Ankara 15. İdari Dava Dairesi-2023/795-..."
    //   "Ankara(Kapatılan) 28. İdare Mahkemesi-2022/793-..."
    // ─────────────────────────────────────────────────────────────
    private static readonly Regex ReddiyatGondericiPattern = new(
        @"^(?<il>\w+)(?:\s*\(Kapatılan\))?\s+(?<no>\d{1,2})\.\s*(?<tip>İdare|İdari|Vergi|İcra|Asliye|Hukuk|Ceza)\s*(?<birimtipi>Mahkemesi|Dava\s*Dairesi)-(?<esas>\d{4}/\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Alıcı pattern: "-İl N. Tip Dairesi-YYYY/NNN" veya kişi adı
    private static readonly Regex ReddiyatAliciPattern = new(
        @"-(?<il>\w+)\s+(?<no>\d{1,2})\.\s*(?<tip>İdare|Vergi|İcra)\s*Dava\s*Dairesi-(?<esas>\d{4}/\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ─────────────────────────────────────────────────────────────
    // Vezne/Ön Bürosu özel pattern (numarasız birimler için)
    // Örnek: "Ankara İdare ve Vergi Mahkemeleri Vezne-2026/1652 -Hatay..."
    // ─────────────────────────────────────────────────────────────
    private static readonly Regex VezneGondericiPattern = new(
        @"^(?<birim>Ankara\s+İdare\s+ve\s+Vergi\s+Mahkemeleri\s+Vezne(?:\s+ve\s+Ön\s+Bürosu)?)[-\s](?<esas>\d{4}/\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);


    /// <summary>
    /// Banka açıklama alanını parse eder.
    /// </summary>
    public ParsedBankaAciklama Parse(string? aciklama)
    {
        if (string.IsNullOrWhiteSpace(aciklama))
            return new ParsedBankaAciklama(null, null, null, null, 0);

        // 1. Anahtar kelimeyi bul
        string? foundKeyword = null;
        int keywordIndex = -1;

        foreach (var keyword in AnkaraKeywords)
        {
            var idx = aciklama.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                foundKeyword = keyword;
                keywordIndex = idx;
                break;
            }
        }

        if (keywordIndex < 0)
        {
            // Anahtar kelime bulunamadı - düşük confidence ile genel arama yap
            return TryGeneralParse(aciklama);
        }

        // 2. Anahtar kelimeden sonraki metni al
        string relevantPart = aciklama.Substring(keywordIndex);

        // 3. Normalize et
        relevantPart = NormalizeText(relevantPart);

        // 4. Esas No bul
        string? esasNo = null;
        var esasMatch = EsasNoPattern.Match(relevantPart);
        if (esasMatch.Success)
        {
            esasNo = esasMatch.Value;
        }

        // 5. Mahkeme bul
        string? mahkeme = ExtractMahkeme(relevantPart);

        // 6. Confidence hesapla
        double confidence = 0;
        if (foundKeyword != null) confidence += 0.2;
        if (mahkeme != null) confidence += 0.3;
        if (esasNo != null) confidence += 0.5;

        return new ParsedBankaAciklama("Ankara", mahkeme, esasNo, foundKeyword, confidence);
    }

    /// <summary>
    /// Anahtar kelime bulunamadığında genel parse dener.
    /// </summary>
    private ParsedBankaAciklama TryGeneralParse(string aciklama)
    {
        var normalized = NormalizeText(aciklama);
        
        // Esas No ara
        string? esasNo = null;
        var esasMatch = EsasNoPattern.Match(normalized);
        if (esasMatch.Success)
            esasNo = esasMatch.Value;

        // Mahkeme ara
        string? mahkeme = ExtractMahkeme(normalized);

        // Ankara geçiyor mu?
        bool hasAnkara = aciklama.Contains("Ankara", StringComparison.OrdinalIgnoreCase) ||
                         aciklama.Contains("ANK", StringComparison.OrdinalIgnoreCase);

        double confidence = 0;
        if (hasAnkara) confidence += 0.1;
        if (mahkeme != null) confidence += 0.2;
        if (esasNo != null) confidence += 0.3;

        string? il = hasAnkara ? "Ankara" : null;

        return new ParsedBankaAciklama(il, mahkeme, esasNo, null, confidence);
    }

    /// <summary>
    /// Metinden mahkeme adını çıkarır.
    /// </summary>
    private static string? ExtractMahkeme(string text)
    {
        // Önce numaralı mahkeme dene
        var match = MahkemePattern.Match(text);
        if (match.Success)
            return match.Value.Trim();

        // Numarasız mahkeme dene
        var altMatch = MahkemeAltPattern.Match(text);
        if (altMatch.Success)
            return altMatch.Value.Trim();

        return null;
    }

    /// <summary>
    /// Metni normalize eder (parsing için hazırlar).
    /// Kullanıcının mevcut algoritmasından alınan dönüşümler.
    /// </summary>
    private static string NormalizeText(string text)
    {
        return text
            // Uzun ifadeleri kısalt
            .Replace("Ankara İdare ve Vergi Mahkemeleri Vezne ve Ön Bürosu", "Ankara Bim VOnB")
            .Replace("Portal Tahsilatıdır", "PortalTahsilat")
            
            // Bölge ifadelerini normalize et
            .Replace("Bölge-Ankara", "Ankara")
            .Replace("Bölge Ankara", "Ankara")
            
            // Dava türlerini kısalt
            .Replace("İdari Dava", "İdariDD")
            .Replace("Vergi Dava", "VergiDD")
            .Replace("İdare Dava", "İdareDD")
            
            // Ek'leri temizle
            .Replace("Dairesi'nin", "Dairesi")
            .Replace("Mahkemesi'nin", "Mahkemesi")
            .Replace("Mahkemesi.", "Mahkemesi")
            .Replace("Mahkemesi,", "Mahkemesi")
            
            // Özel karakterleri temizle
            .Replace(".VN:", " ")
            .Replace(".VN", " ")
            .Replace(".VD:", " ")
            .Replace(".VD", " ");
    }

    /// <summary>
    /// Açıklama metninin Ankara'ya ait olup olmadığını kontrol eder.
    /// </summary>
    public bool IsAnkaraRelated(string? aciklama)
    {
        if (string.IsNullOrWhiteSpace(aciklama))
            return false;

        // Anahtar kelimelerden biri var mı?
        foreach (var keyword in AnkaraKeywords)
        {
            if (aciklama.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
    
    /// <summary>
    /// Reddiyat açıklamasından gönderen birim bilgilerini parse eder.
    /// Format: "Ankara 5. Vergi Mahkemesi-2024/950 Vergi-Ankara 1. Vergi Dava Dairesi-2025/441..."
    /// </summary>
    public ParsedReddiyatGonderici ParseReddiyatGonderici(string? aciklama)
    {
        if (string.IsNullOrWhiteSpace(aciklama))
            return new ParsedReddiyatGonderici(null, null, null, null, 0);

        double confidence = 0;
        string? gonderenMahkeme = null;
        string? gonderenEsasNo = null;
        string? aliciMahkeme = null;
        string? aliciEsasNo = null;

        // Gönderen bilgisi (açıklamanın başında) - Önce standart pattern
        var gonderenMatch = ReddiyatGondericiPattern.Match(aciklama);
        if (gonderenMatch.Success)
        {
            var il = gonderenMatch.Groups["il"].Value;
            var no = gonderenMatch.Groups["no"].Value;
            var tip = gonderenMatch.Groups["tip"].Value;
            var birimTipi = gonderenMatch.Groups["birimtipi"].Value; // "Mahkemesi" veya "Dava Dairesi"
            gonderenMahkeme = $"{il} {no}. {tip} {birimTipi}";
            gonderenEsasNo = gonderenMatch.Groups["esas"].Value;
            confidence += 0.5;
        }
        else
        {
            // Fallback: Vezne/Ön Bürosu özel pattern dene
            var vezneMatch = VezneGondericiPattern.Match(aciklama);
            if (vezneMatch.Success)
            {
                gonderenMahkeme = vezneMatch.Groups["birim"].Value.Trim();
                gonderenEsasNo = vezneMatch.Groups["esas"].Value;
                confidence += 0.5;
            }
        }

        // Alıcı bilgisi (açıklamanın ortasında)
        var aliciMatch = ReddiyatAliciPattern.Match(aciklama);
        if (aliciMatch.Success)
        {
            var il = aliciMatch.Groups["il"].Value;
            var no = aliciMatch.Groups["no"].Value;
            var tip = aliciMatch.Groups["tip"].Value;
            aliciMahkeme = $"{il} {no}. {tip} Dava Dairesi";
            aliciEsasNo = aliciMatch.Groups["esas"].Value;
            confidence += 0.3;
        }

        return new ParsedReddiyatGonderici(
            gonderenMahkeme,
            gonderenEsasNo,
            aliciMahkeme,
            aliciEsasNo,
            confidence);
    }
}

