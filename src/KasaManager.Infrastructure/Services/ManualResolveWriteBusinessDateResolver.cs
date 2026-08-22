#nullable enable
using KasaManager.Application.Abstractions;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// IManualResolveWriteBusinessDateResolver implementasyonu — KasaManager Revision 3 Manual Resolve
/// Write-BusinessDate Surgical Closure. HesapKontrol kayıtlarını sorgulamaz; yalnızca sunucu
/// tarafında analiz edilebilir Excel BusinessDate'i (IKasaReportDateRulesService) persisted Genel
/// Kasa tarihiyle (IKasaRaporSnapshotService) kıyaslar. Sistem saati asla authoritative değildir —
/// DateOnly.MaxValue yalnızca "en son persisted" sorgusunun üst sınırıdır, dönen değer değildir.
/// </summary>
public sealed class ManualResolveWriteBusinessDateResolver : IManualResolveWriteBusinessDateResolver
{
    private readonly IKasaReportDateRulesService _dateRules;
    private readonly IKasaRaporSnapshotService _snapshots;

    public ManualResolveWriteBusinessDateResolver(
        IKasaReportDateRulesService dateRules,
        IKasaRaporSnapshotService snapshots)
    {
        _dateRules = dateRules;
        _snapshots = snapshots;
    }

    public async Task<ManualResolveWriteBusinessDateResult> ResolveAsync(
        string uploadFolderAbsolute, CancellationToken ct = default)
    {
        var eval = await _dateRules.EvaluateAsync(uploadFolderAbsolute, ct);

        if (!eval.ProposedDate.HasValue || eval.RequiresUserDecision)
        {
            return ManualResolveWriteBusinessDateResult.FailClosed(
                eval.HasConflict
                    ? ManualResolveWriteBusinessDateFailureReason.ExcelDateConflict
                    : ManualResolveWriteBusinessDateFailureReason.NoAnalyzableExcelDate,
                "Analiz edilebilir/çakışmasız bir Excel BusinessDate bulunamadı.");
        }

        var excelDate = eval.ProposedDate.Value;

        // Üst sınır: sorgu penceresini sınırlamak için kullanılır, dönen değer DEĞİLDİR.
        var lastKasa = await _snapshots.GetLastGenelKasaSnapshotBeforeOrOnAsync(DateOnly.MaxValue, ct);
        if (lastKasa == null)
        {
            return ManualResolveWriteBusinessDateResult.Ok(
                excelDate, "Persisted Genel Kasa snapshot yok — Excel BusinessDate kullanıldı.");
        }

        if (excelDate == lastKasa.RaporTarihi)
        {
            return ManualResolveWriteBusinessDateResult.FailClosed(
                ManualResolveWriteBusinessDateFailureReason.ExcelDateEqualsPersistedKasa,
                $"Excel tarihi ({excelDate:yyyy-MM-dd}) zaten frozen/persisted Genel Kasa tarihiyle aynı.");
        }

        if (excelDate < lastKasa.RaporTarihi)
        {
            return ManualResolveWriteBusinessDateResult.FailClosed(
                ManualResolveWriteBusinessDateFailureReason.ExcelDateBeforePersistedKasa,
                $"Excel tarihi ({excelDate:yyyy-MM-dd}) persisted Genel Kasa tarihinden ({lastKasa.RaporTarihi:yyyy-MM-dd}) eski.");
        }

        return ManualResolveWriteBusinessDateResult.Ok(
            excelDate,
            $"Excel tarihi ({excelDate:yyyy-MM-dd}) persisted Genel Kasa'dan ({lastKasa.RaporTarihi:yyyy-MM-dd}) sonra.");
    }
}
