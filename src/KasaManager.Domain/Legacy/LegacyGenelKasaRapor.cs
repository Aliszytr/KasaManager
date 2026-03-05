#nullable enable
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KasaManager.Domain.Legacy;

/// <summary>
/// Eski KasaRaporuDB — GenelKasaRaporNesnesi tablosu.
/// Read-only, tablo yapısı aynen korunmuştur.
/// </summary>
[Table("GenelKasaRaporNesnesis")]
public sealed class LegacyGenelKasaRapor
{
    [Key] public Guid Id { get; init; }

    // ── Temel ─────────────────────────────────────────────
    public string? veznedar { get; init; }
    public DateTime raporTarihi { get; init; }
    public double bakiye { get; init; }

    // ── Tahsilat / Harç / Reddiyat ────────────────────────
    public double onlineTahsilat { get; init; }
    public double tahsilat { get; init; }
    public double reddiyat { get; init; }
    public double onlineHarc { get; init; }
    public double harc { get; init; }

    // ── Stopaj / Vergi ────────────────────────────────────
    public double Stopaj { get; init; }
    public double gelirVergisi { get; init; }
    public double damgaVergisi { get; init; }

    // ── İşlem Adetleri ────────────────────────────────────
    public int tahsilatIslemSayisi { get; init; }
    public int reddiyatIslemSayisi { get; init; }
    public int harcIslemSayisi { get; init; }

    // ── POS ───────────────────────────────────────────────
    public double PosHarc { get; init; }
    public double PosTahsilat { get; init; }
}
