#nullable enable
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KasaManager.Domain.Legacy;

/// <summary>
/// Eski KasaRaporuDB — AksamKasaNesnesi tablosu.
/// Read-only, tablo yapısı aynen korunmuştur.
/// </summary>
[Table("AksamKasaNesnesis")]
public sealed class LegacyAksamKasa
{
    [Key] public Guid Id { get; init; }

    // ── Tarih & Kişi ──────────────────────────────────────
    public DateTime IslemTarihiTahsilat { get; init; }
    public string? KasayiYapan { get; init; }
    public string? Aciklama { get; init; }

    // ── Kasa ──────────────────────────────────────────────
    public double DundenDevredenKasa { get; init; }
    public double DundenDevredenBanka { get; init; }
    public double YarinaDeverecekBanka { get; init; }
    public double BankayaGiren { get; init; }
    public double BankadanCikan { get; init; }
    public double BankadanCekilen { get; init; }

    // ── Tahsilat & Harç ──────────────────────────────────
    public double KaydenTahsilat { get; init; }
    public double KaydenHarc { get; init; }
    public double NormalTahsilat { get; init; }
    public double NormalHarc { get; init; }

    // ── Reddiyat & Stopaj ─────────────────────────────────
    public double NormalReddiyat { get; init; }
    public double OnlineReddiyat { get; init; }
    public double OnlineStopaj { get; init; }
    public double NormalStopaj { get; init; }

    // ── Genel Kasa ────────────────────────────────────────
    public double GenelKasa { get; init; }
    public double BozukParaHaricKasa { get; init; }
    public double VergiKasa { get; init; }
    public double VergiGelenKasa { get; init; }
    public double UyapBakiye { get; init; }

    // ── Bankaya Yatırılacak ───────────────────────────────
    public double BankayaYatirilacakNakit { get; init; }
    public double BankayaYatirilacakStopaj { get; init; }
    public double BankayaYatirilacakHarc { get; init; }

    // Not: Bu sütun adında encoding sorunu var (veritabanında öyle).
    [Column("BankayaYatirilacakHarcıDegistir")]
    public double BankayaYatirilacakHarciDegistir { get; init; }

    public double BankayaYatirilacakTahsilatiDegistir { get; init; }

    // ── CN (Bankadan Çıkamayan) ───────────────────────────
    public double CesitliNedenlerleBankadanCikamayaTahsilat { get; init; }
    public double CesitliNedenlerleBankadanCikamayaHarc { get; init; }

    // Not: Bu sütun adında da encoding sorunu var.
    [Column("BankayaGonderilmişDeger")]
    public double BankayaGonderilmisDeger { get; init; }

    public double BankaGoturulecekNakit { get; init; }
}
