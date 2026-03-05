using KasaManager.Application.Orchestration.Dtos;
using KasaManager.Domain.Settings;

namespace KasaManager.Application.Orchestration.Helpers;

/// <summary>
/// DRY: Global defaults değerlerini DTO'ya uygulamak için tek yer.
/// Daha önce 3 farklı yerde tekrarlanan hydration kodu buraya taşındı.
/// </summary>
public static class KasaDefaultsHydrator
{
    /// <summary>
    /// Global varsayılan değerleri DTO'ya yükler.
    /// BozukPara/NakitPara/GelmeyenD gibi nullable alanlar
    /// sadece null iseler doldurulur (kullanıcı override'ı korunur).
    /// </summary>
    public static void Apply(KasaPreviewDto dto, KasaGlobalDefaultsSettings defaults)
    {
        // Varsayılan değerleri ata (referans gösterimi)
        dto.DefaultBozukPara = defaults.DefaultBozukPara;
        dto.DefaultNakitPara = defaults.DefaultNakitPara;
        dto.DefaultGenelKasaDevredenSeed = defaults.DefaultGenelKasaDevredenSeed;
        dto.DefaultKaydenTahsilat = defaults.DefaultKaydenTahsilat;
        dto.DefaultDundenDevredenKasaNakit = defaults.DefaultDundenDevredenKasaNakit;
        dto.DefaultGenelKasaBaslangicTarihiSeed = defaults.DefaultGenelKasaBaslangicTarihiSeed.HasValue
            ? DateOnly.FromDateTime(defaults.DefaultGenelKasaBaslangicTarihiSeed.Value)
            : null;

        // Kullanıcı override'ı yoksa varsayılan ata
        if (dto.BozukPara == null && defaults.DefaultBozukPara != null)
            dto.BozukPara = defaults.DefaultBozukPara;
        dto.BozukPara ??= 0m;

        if (dto.NakitPara == null && defaults.DefaultNakitPara != null)
            dto.NakitPara = defaults.DefaultNakitPara;
        dto.NakitPara ??= 0m;

        dto.GelmeyenD ??= 0m;
    }
}
