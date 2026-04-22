using System;
using System.Threading;
using System.Threading.Tasks;

namespace KasaManager.Application.Abstractions;

public enum CarryoverScope
{
    /// <summary>
    /// Kasa hesap formüllerindeki ana devreden (Genel Kasa Devreden)
    /// </summary>
    GenelKasa,
    
    /// <summary>
    /// Akşam kasa ekranında kullanılan spesifik devreden nakit
    /// </summary>
    AksamKasaNakit,

    /// <summary>
    /// Sabah kasa ekranında kullanılan spesifik devreden nakit
    /// </summary>
    SabahKasaNakit,

    /// <summary>
    /// Geçmiş günler için Vergi Kasa Bakiye Toplam manuel girişi (snapshot'tan okuma)
    /// </summary>
    VergiKasaSelectionTotal
}

public sealed record CarryoverResolutionResult(
    decimal Value,
    string TargetKey,
    DateOnly RangeStart,
    DateOnly? SourceDate,
    string SourceCode,
    string Reason,
    bool UsedFallback,
    Guid? SourceId = null
);

/// <summary>
/// R6-AUDIT P1(B): Carryover (Devreden) karar zincirini tekilleştiren resolver.
/// Dağınık seed/snapshot/calc fallback mantıklarını merkezi bir karara bağlar.
/// </summary>
public interface ICarryoverResolver
{
    Task<CarryoverResolutionResult> ResolveAsync(DateOnly targetDate, CarryoverScope scope, CancellationToken ct = default);
}
