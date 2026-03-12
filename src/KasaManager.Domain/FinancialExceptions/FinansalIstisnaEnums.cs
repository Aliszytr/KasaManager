#nullable enable
namespace KasaManager.Domain.FinancialExceptions;

/// <summary>
/// İstisna türü — operasyonel köken tanımı.
/// </summary>
public enum IstisnaTuru
{
    /// <summary>Planlanan virman/transfer bankada gerçekleşmedi</summary>
    BasarisizVirman = 0,
    /// <summary>EFT iade geldi ama sisteme henüz girilmedi</summary>
    SistemeGirilmeyenEft = 1,
    /// <summary>Banka hareketi geç yansıdı</summary>
    GecikmeliBankaHareketi = 2,
    /// <summary>Kısmi tutar girildi, kalan bekliyor</summary>
    KismiIslem = 3,
    /// <summary>Çeşitli nedenlerle bankadan çıkamayan tutar</summary>
    BankadanCikamayanTutar = 4
}

/// <summary>
/// Fonksiyonel gruplama.
/// </summary>
public enum IstisnaKategorisi
{
    /// <summary>Başarısız virman / transfer</summary>
    BankaTransferHatasi = 0,
    /// <summary>Sisteme girilmeyen EFT, havale</summary>
    BekleyenSistemGirisi = 1,
    /// <summary>Kısmi tutar girilmiş</summary>
    KismiIslenmis = 2,
    /// <summary>Ertesi gün bankaya yansıma</summary>
    GecikmeliYansima = 3,
    /// <summary>Artık aktif etkisi yok</summary>
    Cozulmus = 4
}

/// <summary>
/// Operasyonel nakit etkisi (mutabakat farkı DEĞİL).
/// Fiziki kasadaki nakit üzerindeki etkiyi temsil eder.
/// </summary>
public enum KasaEtkiYonu
{
    /// <summary>Kasadaki fiziki nakit beklenenden FAZLA</summary>
    Artiran = 0,
    /// <summary>Kasadaki fiziki nakit beklenenden AZ</summary>
    Azaltan = 1,
    /// <summary>Çözülmüş — etkisi sıfır</summary>
    Notr = 2
}

/// <summary>
/// Operatör karar durumu — hesaplamaya katılım kontrolü.
/// Yalnızca Onaylandi durumundaki kayıtlar hesaplamaya katılır.
/// </summary>
public enum KararDurumu
{
    /// <summary>Tespit edildi — henüz onaylanmadı → hesaplamaya KATILMAZ</summary>
    IncelemeBekliyor = 0,
    /// <summary>Operatör onayladı → hesaplamaya KATILIR</summary>
    Onaylandi = 1,
    /// <summary>Operatör reddetti → hesaplamaya KATILMAZ</summary>
    Reddedildi = 2
}

/// <summary>
/// Yaşam döngüsü durumu.
/// </summary>
public enum IstisnaDurumu
{
    /// <summary>Yeni — aktif etki</summary>
    Acik = 0,
    /// <summary>Kısmen işlendi — kalan tutar aktif</summary>
    KismiCozuldu = 1,
    /// <summary>Tamamen çözüldü — etkisi sıfır</summary>
    Cozuldu = 2,
    /// <summary>Bilinçli olarak ertesi güne taşındı — aynı gün pasif</summary>
    ErtesiGuneDevredildi = 3,
    /// <summary>Yanlış giriş — hiç etkisi yok</summary>
    Iptal = 4
}
