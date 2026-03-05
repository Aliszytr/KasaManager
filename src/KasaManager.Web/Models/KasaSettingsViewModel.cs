using System.Text.Json;

namespace KasaManager.Web.Models;

/// <summary>
/// R9.1: Ayarlar ekranı (global).
/// - Vergi Kasa veznedarları (varsayılan seçim)
/// - Varsayılan Nakit / Bozuk para
/// </summary>
public sealed class KasaSettingsViewModel
{
    public DateOnly? OptionsSourceDate { get; set; }

    /// <summary>
    /// UI'de checkbox listesi için mevcut veznedar isimleri.
    /// Kaynak: DB'deki en son Genel snapshot.
    /// </summary>
    public List<string> VeznedarOptions { get; set; } = new();

    /// <summary>
    /// Ayarlardan seçili Vergi Kasa veznedarları.
    /// </summary>
    public List<string> SelectedVergiKasaVeznedarlar { get; set; } = new();

    public decimal? DefaultNakitPara { get; set; }
    public decimal? DefaultBozukPara { get; set; }

    /// <summary>
    /// R10: Kasada Eksik/Fazla (varsayılan). Genel Kasa raporunda T.Ek.Faz.Mk alanına otomatik yazılır.
    /// </summary>
    public decimal? DefaultKasaEksikFazla { get; set; }

    /// <summary>
    /// (R10.4) İlk Genel Kasa çalıştırmasında (DB'de önceki Genel snapshot yokken)
    /// kullanılacak başlangıç devreden (seed) değeri.
    /// </summary>
    public decimal? DefaultGenelKasaDevredenSeed { get; set; }

    /// <summary>
    /// (R10.9) İlk Genel Kasa çalıştırmasında (DB boşken) kullanılacak başlangıç tarihidir (seed).
    /// </summary>
    public DateOnly? DefaultGenelKasaBaslangicTarihiSeed { get; set; }

    /// <summary>
    /// (R10.9) Kayden Tahsilat için opsiyonel manuel/override değer.
    /// Boş ise MasrafveReddiyat.xlsx içinden hesaplanan değer kullanılır.
    /// </summary>
    public decimal? DefaultKaydenTahsilat { get; set; }

    /// <summary>
    /// (R14C) Dünden Devreden Kasa (Override).
    /// 0/boş ise önceki kasa kaydından/snapshot'ından otomatik fallback uygulanır.
    /// </summary>
    public decimal? DefaultDundenDevredenKasaNakit { get; set; }

    // ===== Banka Hesap IBAN Bilgileri =====
    public string? HesapAdiStopaj { get; set; }
    public string? IbanStopaj { get; set; }
    public string? HesapAdiMasraf { get; set; }
    public string? IbanMasraf { get; set; }
    public string? HesapAdiHarc { get; set; }
    public string? IbanHarc { get; set; }
    public string? IbanPostaPulu { get; set; }

    // ===== Banka Yazıları Şablonları =====
    public List<KasaManager.Domain.Reports.DocumentTemplate> DocumentTemplates { get; set; } = new();

    // UI mesajları
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    public static List<string> TryParseSelected(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json ?? "[]") ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
