#nullable enable
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KasaManager.Domain.Legacy;

/// <summary>
/// Eski KasaRaporuDB — SabahKasaNesnesis tablosu.
/// Read-only, tablo yapısı aynen korunmuştur.
/// </summary>
[Table("SabahKasaNesnesis")]
public sealed class LegacySabahKasa
{
    [Key] public Guid Id { get; init; }

    // ── Tarih & Kişi ──────────────────────────────────────
    public DateTime IslemTarihiTahsilatSabahK { get; init; }
    public string? KasayiYapanSabahK { get; init; }
    public string? AciklamaSabahK { get; init; }

    // ── Banka Tahsilat ────────────────────────────────────
    public double DundenDevredenBankaTahsilatSabahK { get; init; }
    public double YarinaDeverecekBankaTahsilatSabahK { get; init; }
    public double BankaGirenTahsilatSabahK { get; init; }
    public double BankaCikanTahsilatSabahK { get; init; }
    public double BankaCekilenTahsilatSabahK { get; init; }

    // ── Banka Harç ────────────────────────────────────────
    public double DundenDevredenBankaHarcSabahK { get; init; }
    public double YarinaDeverecekBankaHarcSabahK { get; init; }
    public double BankaGirenHarcSabahK { get; init; }
    public double BankaCikanHarcSabahK { get; init; }
    public double BankaCekilenHarcSabahK { get; init; }

    // ── Gelmeyen / Eksik-Fazla ────────────────────────────
    public double GelmeyenTahsilatSabahK { get; init; }
    public double GelmeyenHarcSabahK { get; init; }
    public double DundenEksikFazlaTahsilatSabahK { get; init; }
    public double GuneAitEksikFazlaTahsilatSabahK { get; init; }
    public double DundenEksikFazlaGelenTahsilatSabahK { get; init; }
    public double DundenEksikFazlaHarcSabahK { get; init; }
    public double GuneAitEksikFazlaHarcSabahK { get; init; }
    public double DundenEksikFazlaGelenHarcSabahK { get; init; }

    // ── Tahsilat ──────────────────────────────────────────
    public double NormalTahsilatSabahK { get; init; }
    public double OnlineTahsilatSabahK { get; init; }
    public double KaydenTahsilatSabahK { get; init; }

    // ── Harç ──────────────────────────────────────────────
    public double KaydenHarcSabahK { get; init; }
    public double NormalHarcSabahK { get; init; }
    public double OnlineHarcSabahK { get; init; }

    // ── Stopaj & Reddiyat ─────────────────────────────────
    public double OnlineStopajSabahK { get; init; }
    public double NormalStopajSabahK { get; init; }
    public double OnlineReddiyatSabahK { get; init; }
    public double NormalReddiyatSabahK { get; init; }

    // ── Kasa & Genel ──────────────────────────────────────
    public double GenelKasaArtiEksiSabahK { get; init; }
    public double UyapBakiyeSabahK { get; init; }
    public double DundenDevredenKasaSabahK { get; init; }
    public double GenelKasaSabahK { get; init; }
    public double BozukParaHaricKasaSabahK { get; init; }
    public double VergiKasaSabahK { get; init; }
    public double VergiGelenKasaSabahK { get; init; }

    // ── CN (Bankadan Çıkamayan) ───────────────────────────
    public double CNBankadanCikamayanTahsilatSabahK { get; init; }
    public double CNBankadanCikamayanHarcSabahK { get; init; }

    // ── Bankaya Yatırılacak ───────────────────────────────
    public double BankayaYatirilacakNakitSabahK { get; init; }
    public double BankayaYatirilacakStopajSabahK { get; init; }
    public double BankayaYatirilacakHarcSabahK { get; init; }
    public double GelenEFTIadeSabahK { get; init; }

    // Not: Bu sütun adında encoding sorunu var (veritabanında öyle).
    // EF Core convention ile otomatik eşleşir.
    [Column("BankayaYatirilacakHarcıDegistirSabahK")]
    public double BankayaYatirilacakHarciDegistirSabahK { get; init; }

    public double BankayaYatirilacakTahsilatiDegistirSabahK { get; init; }
    public double BToplamYATANSabahK { get; init; }
}
