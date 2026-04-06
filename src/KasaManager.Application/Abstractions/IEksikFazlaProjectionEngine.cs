#nullable enable
using KasaManager.Domain.Projection;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// P2: EksikFazla Projection Engine interface.
/// 
/// Legacy ComputeEksikFazlaChainAsync ile aynı çıktı şeklini üretir, ama:
/// - CalculatedSnapshot / Pool hybrid kaynak kullanır (snapshot yerine)
/// - Iterative çalışır (recursive yerine)
/// - Her gün için açık kaynak bilgisi (audit trail) tutar
/// - HesapKontrol düzeltmelerini engine içinde uygular
///
/// Shadow modda: legacy sonuç korunur, bu engine paralel çalışır, diff loglanır.
/// </summary>
public interface IEksikFazlaProjectionEngine
{
    Task<ProjectionResult> ProjectAsync(
        ProjectionRequest request,
        CancellationToken ct = default);
}
