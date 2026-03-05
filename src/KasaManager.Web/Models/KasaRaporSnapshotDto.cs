using System.ComponentModel.DataAnnotations;
using KasaManager.Domain.Reports;

namespace KasaManager.Web.Models;

/// <summary>
/// R6: UI'dan Snapshot "Kaydet" için kullanılan DTO.
/// Import/Preview aşamasında DB'ye yazılmaz; sadece Save endpoint'i yazım yapar.
/// </summary>
public sealed class KasaRaporSnapshotDto
{
    [Required]
    public DateOnly RaporTarihi { get; set; }

    [Required]
    public KasaRaporTuru RaporTuru { get; set; }

    /// <summary>
    /// Snapshot şeması/sürümü. (Örn: 6)
    /// </summary>
    public int Version { get; set; } = 6;

    /// <summary>
    /// Seçili satırların toplamı (şimdilik 0 gönderilebilir).
    /// </summary>
    public decimal SelectionTotal { get; set; } = 0m;

    /// <summary>UI textbox/ayar değerleri (serbest JSON)</summary>
    public string? InputsJson { get; set; }

    /// <summary>Hesap sonucu/özet değerler (serbest JSON)</summary>
    public string? ResultsJson { get; set; }

    /// <summary>UI uyarıları (serbest JSON)</summary>
    public string? WarningsJson { get; set; }

    [Required]
    public List<KasaRaporSnapshotRowDto> Rows { get; set; } = new();
}

public sealed class KasaRaporSnapshotRowDto
{
    public string? Veznedar { get; set; }

    public bool IsSelected { get; set; }

    public decimal? Bakiye { get; set; }

    public bool IsSummaryRow { get; set; }

    /// <summary>Satırdaki kolon değerleri (canonical -> value) JSON</summary>
    [Required]
    public string ColumnsJson { get; set; } = "{}";

    /// <summary>Kolon başlıkları (canonical + original header + index) JSON</summary>
    public string? HeadersJson { get; set; }
}
