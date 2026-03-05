using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace KasaManager.Web.Controllers;

/// <summary>
/// R6: Snapshot kasa raporu.
/// Import/Preview DB'ye yazmaz; sadece "Kaydet" bu controller üzerinden snapshot yazar.
/// </summary>
[Authorize]
public sealed class KasaRaporController : Controller
{
    private readonly IKasaRaporSnapshotService _snapshots;

    public KasaRaporController(IKasaRaporSnapshotService snapshots)
    {
        _snapshots = snapshots;
    }

    [HttpGet]
    public async Task<IActionResult> ViewSnapshot(DateOnly raporTarihi, KasaRaporTuru raporTuru, CancellationToken ct)
    {
        var snap = await _snapshots.GetAsync(raporTarihi, raporTuru, ct);
        if (snap == null) return NotFound();
        return View("ViewSnapshot", snap);
    }

    /// <summary>
    /// UI "Kaydet" butonu buraya POST eder.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] KasaRaporSnapshotDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var snapshot = Map(dto, User?.Identity?.Name);
        var saved = await _snapshots.SaveAsync(snapshot, ct);

        // Kaydettikten sonra görüntülemeye yönlendir
        return RedirectToAction(nameof(ViewSnapshot), new { raporTarihi = saved.RaporTarihi, raporTuru = saved.RaporTuru });
    }

    private static KasaRaporSnapshot Map(KasaRaporSnapshotDto dto, string? createdBy)
    {
        KasaRaporSnapshotInputs? inputs = null;
        if (!string.IsNullOrWhiteSpace(dto.InputsJson))
        {
            inputs = new KasaRaporSnapshotInputs
            {
                ValuesJson = dto.InputsJson
            };
        }

        KasaRaporSnapshotResults? results = null;
        if (!string.IsNullOrWhiteSpace(dto.ResultsJson))
        {
            results = new KasaRaporSnapshotResults
            {
                ValuesJson = dto.ResultsJson
            };
        }

        var rows = dto.Rows.Select(r => new KasaRaporSnapshotRow
        {
            Veznedar = r.Veznedar,
            IsSelected = r.IsSelected,
            Bakiye = r.Bakiye,
            IsSummaryRow = r.IsSummaryRow,
            ColumnsJson = string.IsNullOrWhiteSpace(r.ColumnsJson) ? "{}" : r.ColumnsJson,
            HeadersJson = string.IsNullOrWhiteSpace(r.HeadersJson) ? null : r.HeadersJson
        }).ToList();

        return new KasaRaporSnapshot
        {
            RaporTarihi = dto.RaporTarihi,
            RaporTuru = dto.RaporTuru,

            // ✅ Tipler düzeltildi: int ve decimal
            Version = dto.Version,
            SelectionTotal = dto.SelectionTotal,

            CreatedBy = createdBy,
            WarningsJson = string.IsNullOrWhiteSpace(dto.WarningsJson) ? null : dto.WarningsJson,

            Rows = rows,
            Inputs = inputs,
            Results = results
        };
    }
}
