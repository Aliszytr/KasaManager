namespace KasaManager.Domain.Settings;

/// <summary>
/// R9: Global (kullanıcıdan bağımsız) kasa varsayılanları.
/// - Vergi Kasa veznedar checkbox seçimleri
/// - (R9.1 vizyon) Kasadaki varsayılan Nakit/Bozuk para tutarları
///
/// Not: Bu ayarlar tarih bazlı değildir. Değiştiği anda gelecek tüm kasalara uygulanır.
/// </summary>
public sealed class KasaGlobalDefaultsSettings
{
    /// <summary>
    /// Singleton settings kaydının sabit Id'si.
    /// </summary>
    public const int SingletonId = 1;

    public int Id { get; set; }

    /// <summary>
    /// Vergi Kasa veznedarlarının seçimi (JSON string).
    /// Örn: ["Ali","Veznedar2"]
    /// </summary>
    public string SelectedVeznedarlarJson { get; set; } = "[]";

    /// <summary>
    /// (R9.1) Varsayılan bozuk para.
    /// </summary>
    public decimal? DefaultBozukPara { get; set; }

    /// <summary>
    /// (R9.1) Varsayılan nakit para.
    /// </summary>
    public decimal? DefaultNakitPara { get; set; }

    /// <summary>
    /// (R10) Varsayılan "Kasada Eksik/Fazla" değeri.
    /// Kullanıcı her raporda ayrı girmek zorunda kalmasın diye global ayarlardan okunur.
    /// </summary>
    public decimal? DefaultKasaEksikFazla { get; set; }

    /// <summary>
    /// (R10.4) İlk Genel Kasa çalıştırmasında (DB'de önceki Genel Kasa snapshot'ı yokken)
    /// kullanıcının manuel olarak girdiği başlangıç devreden değeridir (seed).
    ///
    /// Not:
    /// - İlk kayıt yapıldıktan sonra Devreden mantığı artık "bir önceki Genel Kasa" üzerinden yürür.
    /// - Bu alan sadece ilk kurulum / veri tabanı boş senaryosunda kullanıcıyı kurtarmak içindir.
    /// </summary>
    public decimal? DefaultGenelKasaDevredenSeed { get; set; }

    
    /// <summary>
    /// (R10.9) İlk Genel Kasa çalıştırmasında (DB'de önceki Genel Kasa snapshot'ı yokken)
    /// kullanılacak başlangıç tarihidir (seed).
    /// 
    /// Not:
    /// - DB'de son Genel Kasa tarihi varsa, başlangıç her zaman (son + 1 gün) olur.
    /// - Bu alan sadece ilk kurulum / DB boş senaryosunda kullanıcıyı kurtarmak içindir.
    /// </summary>
    public DateTime? DefaultGenelKasaBaslangicTarihiSeed { get; set; }

    /// <summary>
    /// (R10.9) Kayden Tahsilat bazı senaryolarda rapordan güvenilir okunamayabilir.
    /// İstenirse buradan manuel/override girilebilir.
    /// Boş ise MasrafveReddiyat.xlsx içinden hesaplanan değer kullanılır.
    /// </summary>
    public decimal? DefaultKaydenTahsilat { get; set; }

    /// <summary>
    /// (R14C) Dünden Devreden Kasa (Override).
    /// Şimdilik Ayarlardan girilir.
    /// 0/boş ise, ilgili kasa türünün bir önceki tarihli kaydından/snapshot'ından fallback ile bulunur.
    /// </summary>
    public decimal? DefaultDundenDevredenKasaNakit { get; set; }

    // ===== Banka Hesap IBAN Bilgileri =====

    /// <summary>
    /// Stopaj hesabı — banka/hesap adı (örn: "Ziraat - Stopaj").
    /// </summary>
    public string? HesapAdiStopaj { get; set; }

    /// <summary>
    /// Stopaj hesabı — IBAN numarası (normalize edilir: boşluk/tire silinir, UPPER).
    /// </summary>
    public string? IbanStopaj { get; set; }

    /// <summary>
    /// Tahsilat (Masraf) hesabı — banka/hesap adı.
    /// </summary>
    public string? HesapAdiMasraf { get; set; }

    /// <summary>
    /// Tahsilat (Masraf) hesabı — IBAN numarası.
    /// </summary>
    public string? IbanMasraf { get; set; }

    /// <summary>
    /// Harç hesabı — banka/hesap adı.
    /// </summary>
    public string? HesapAdiHarc { get; set; }

    /// <summary>
    /// Harç hesabı — IBAN numarası.
    /// </summary>
    public string? IbanHarc { get; set; }

    /// <summary>
    /// Posta Pulu hesabı — IBAN numarası (Genel Kasa bağlamında).
    /// </summary>
    public string? IbanPostaPulu { get; set; }


    // ===== Vergide Biriken Seed =====

    /// <summary>
    /// Genel Kasa Raporu'ndan aktarılan "Vergide Biriken" başlangıç değeri (seed).
    /// Sabah/Akşam kasada formül: VergideBiriken = Seed + VergiKasa − VergidenGelen
    /// Rapor kaydedildiğinde sonuç otomatik yeni seed olur (carry-forward).
    /// </summary>
    public decimal? VergideBirikenSeed { get; set; }

    /// <summary>
    /// Vergide Biriken seed'in son güncellenme zamanı (audit).
    /// </summary>
    public DateTime? VergideBirikenSeedUpdatedAt { get; set; }


    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    public string? UpdatedBy { get; set; }
}
