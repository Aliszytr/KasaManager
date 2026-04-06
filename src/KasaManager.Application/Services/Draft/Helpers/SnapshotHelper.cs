#nullable enable
using System.Globalization;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;

namespace KasaManager.Application.Services.Draft.Helpers;

/// <summary>
/// Snapshot okuma ve devreden hesaplama işlemleri için yardımcı sınıf.
/// KasaDraftService'ten çıkarıldı - R1 Refactoring.
/// </summary>
public class SnapshotHelper
{
    // P4.2: Snapshot okuma işlemi tamamen kapatıldı.
    // Nesne sadece UnifiedPoolEntry factory olarak görev yapıyor.
    public SnapshotHelper()
    {
    }

    // P4.2: TryReadDateOnlyFromSnapshotInputs, TryReadDecimalFromSnapshotResults, TryReadDevredenKasaFromPreviousAksamSnapshotAsync fonksiyonları Stateless mimari gereği silinmiştir.


    /// <summary>
    /// UnifiedPool entry oluşturur (raw).
    /// </summary>
    public static UnifiedPoolEntry CreateRawEntry(
        string canonicalKey,
        decimal value,
        string sourceName,
        string? sourceFile = null,
        string? notes = null)
    {
        return new UnifiedPoolEntry
        {
            CanonicalKey = canonicalKey,
            Value = value.ToString("N2", CultureInfo.InvariantCulture),
            Type = UnifiedPoolValueType.Raw,
            SourceName = sourceName,
            SourceFile = sourceFile,
            Notes = notes,
            IncludeInCalculations = true
        };
    }

    /// <summary>
    /// UnifiedPool entry oluşturur (override).
    /// </summary>
    public static UnifiedPoolEntry CreateOverrideEntry(
        string canonicalKey,
        decimal value,
        string sourceName,
        string? sourceFile = null,
        string? notes = null)
    {
        return new UnifiedPoolEntry
        {
            CanonicalKey = canonicalKey,
            Value = value.ToString("N2", CultureInfo.InvariantCulture),
            Type = UnifiedPoolValueType.Override,
            SourceName = sourceName,
            SourceFile = sourceFile,
            Notes = notes,
            IncludeInCalculations = true
        };
    }

    /// <summary>
    /// UnifiedPool entry oluşturur (derived).
    /// </summary>
    public static UnifiedPoolEntry CreateDerivedEntry(
        string canonicalKey,
        decimal value,
        string sourceName,
        string? sourceDetails = null,
        string? notes = null)
    {
        return new UnifiedPoolEntry
        {
            CanonicalKey = canonicalKey,
            Value = value.ToString("N2", CultureInfo.InvariantCulture),
            Type = UnifiedPoolValueType.Derived,
            SourceName = sourceName,
            SourceDetails = sourceDetails,
            Notes = notes,
            IncludeInCalculations = true
        };
    }
}
