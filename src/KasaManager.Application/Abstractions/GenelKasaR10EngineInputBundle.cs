#nullable enable
namespace KasaManager.Application.Abstractions;

/// <summary>
/// FAZ-2 / Adım-1: GenelKasaRapor ekranı "UI-only" çalışabilsin diye,
/// Application katmanında üretilen (UnifiedPool + Issues + meta) paket.
/// 
/// Not: Bu tip Abstractions içinde tutulur; böylece Web/Controller katmanı sadece Abstractions'a
/// referans verir ve Models/Services namespace drift oluşmaz.
/// </summary>
public sealed class GenelKasaR10EngineInputBundle
{
    public required DateOnly BaslangicTarihi { get; init; }
    public required DateOnly BitisTarihi { get; init; }
    public required DateOnly DevredenSonTarihi { get; init; }

    public required IReadOnlyList<UnifiedPoolEntry> PoolEntries { get; init; }

    /// <summary>
    /// Debug amaçlı: MasrafveReddiyat ve/veya BankaTahsilat örnek satırları.
    /// </summary>
    public string? RawJson { get; init; }

    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
}
