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
    private readonly IKasaRaporSnapshotService _snapshots;

    public SnapshotHelper(IKasaRaporSnapshotService snapshots)
    {
        _snapshots = snapshots;
    }

    /// <summary>
    /// Snapshot inputs'tan DateOnly değerini okumayı dener.
    /// </summary>
    public static DateOnly? TryReadDateOnlyFromSnapshotInputs(KasaRaporSnapshot snapshot, string[] possibleKeys)
    {
        if (snapshot.Inputs?.ValuesJson is null) return null;

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(snapshot.Inputs.ValuesJson);
            if (dict is null) return null;

            foreach (var key in possibleKeys)
            {
                if (dict.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
                {
                    if (DateParsingHelper.TryParseDateOnly(raw, out var d))
                        return d;
                }
            }
        }
        catch (JsonException) { /* JSON bozuk — null dönecek, caller handle eder */ }

        return null;
    }

    /// <summary>
    /// Snapshot results'tan decimal değerini okumayı dener.
    /// </summary>
    public static decimal? TryReadDecimalFromSnapshotResults(KasaRaporSnapshot snapshot, string[] possibleKeys)
    {
        if (snapshot.Results?.ValuesJson is null) return null;

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(snapshot.Results.ValuesJson);
            if (dict is null) return null;

            foreach (var key in possibleKeys)
            {
                if (dict.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
                {
                    if (DecimalParsingHelper.TryParseDecimal(raw, out var v))
                        return v;
                }
            }
        }
        catch (JsonException) { /* JSON bozuk — null dönecek, caller handle eder */ }

        return null;
    }

    /// <summary>
    /// Bir önceki akşam snapshot'ından devreden kasa değerini alır.
    /// </summary>
    public async Task<decimal?> TryReadDevredenKasaFromPreviousAksamSnapshotAsync(
        DateOnly raporTarihi,
        CancellationToken ct)
    {
        var prevDate = raporTarihi.AddDays(-1);
        var aksamSnap = await _snapshots.GetAsync(prevDate, KasaRaporTuru.Aksam, ct);

        if (aksamSnap is not null)
        {
            var val = TryReadDecimalFromSnapshotResults(aksamSnap, new[] { "GenelKasa", "Genel Kasa" });
            if (val is not null)
                return val;
        }

        return null;
    }

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
