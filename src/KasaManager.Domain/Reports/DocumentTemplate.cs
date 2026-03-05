namespace KasaManager.Domain.Reports;

/// <summary>
/// Banka resmi yazıları, para çekme yazıları vb. için yeniden kullanılabilir belge şablonu.
/// Tüm alanlar serbest düzenlenebilir — başlık/format kısıtlaması yoktur.
/// Placeholder'lar: {{Tarih}}, {{Tutar}}, {{BankaAdi}}, {{IBAN}}, {{KasaTuru}}, {{Hazirlayan}} vb.
/// </summary>
public sealed class DocumentTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Şablon adı: "Para Çekme Yazısı", "Banka Havale Talimatı"</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Kategori: BankaYazisi, ResmiYazi, ParaCekme, HavaleTalimati</summary>
    public string Category { get; set; } = "BankaYazisi";

    /// <summary>Sayfa üst başlık metni — çok satırlı, kurum adı vb. (Ör: T.C.\nANKARA BÖLGE İDARE MAHKEMESİ\nVEZNE ÖN BÜRO)</summary>
    public string? HeaderText { get; set; }

    /// <summary>Sayı/Konu bloğu — çok satırlı (Ör: Sayı : 2026/.....Muh\nKonu : Para Çekme Yazısı)</summary>
    public string? SayiKonuText { get; set; }

    /// <summary>Muhatap bloğu — banka/kurum adı + adres (Ör: TÜRKİYE VAKIFLAR BANKASI\nBÖLGE İDARE MAHKEMESİ ŞUBESİNE\nANKARA)</summary>
    public string? MuhatapText { get; set; }

    /// <summary>Şablon gövde metni — placeholder'lı</summary>
    public string BodyTemplate { get; set; } = string.Empty;

    /// <summary>İmza bloğu — çok satırlı, unvan + isim (Ör: {{Hazirlayan}}\nVezne Memuru)</summary>
    public string? ImzaBlokuText { get; set; }

    /// <summary>Sayfa altı (footer) metni — çok satırlı</summary>
    public string? FooterText { get; set; }

    /// <summary>Aktif/pasif durumu</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

