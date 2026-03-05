#nullable enable
using KasaManager.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KasaManager.Web.Controllers;

/// <summary>
/// Eski KasaRaporuDB veritabanındaki tarihsel verilerin görüntülendiği controller.
/// Tamamen read-only — veri düzenleme veya silme yapılamaz.
/// </summary>
[Authorize]
public class LegacyKasaController : Controller
{
    private readonly ILegacyKasaService _legacy;

    public LegacyKasaController(ILegacyKasaService legacy) => _legacy = legacy;

    /// <summary>
    /// Tab yapısıyla Sabah/Akşam/Genel Kasa verilerini listeler.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(
        string tab = "sabah",
        DateTime? startDate = null,
        DateTime? endDate = null,
        int page = 1,
        CancellationToken ct = default)
    {
        ViewBag.ActiveTab = tab;
        ViewBag.StartDate = startDate;
        ViewBag.EndDate = endDate;
        ViewBag.Page = page;

        const int pageSize = 25;

        return tab.ToLowerInvariant() switch
        {
            "aksam" => View("Index", new LegacyIndexViewModel
            {
                Tab = "aksam",
                AksamResult = await _legacy.GetAksamKasaListAsync(startDate, endDate, page, pageSize, ct)
            }),
            "genel" => View("Index", new LegacyIndexViewModel
            {
                Tab = "genel",
                GenelResult = await _legacy.GetGenelKasaRaporListAsync(startDate, endDate, page, pageSize, ct)
            }),
            _ => View("Index", new LegacyIndexViewModel
            {
                Tab = "sabah",
                SabahResult = await _legacy.GetSabahKasaListAsync(startDate, endDate, page, pageSize, ct)
            })
        };
    }

    /// <summary>
    /// Sabah Kasa detayı.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SabahKasaDetail(Guid id, CancellationToken ct)
    {
        var item = await _legacy.GetSabahKasaByIdAsync(id, ct);
        if (item is null) return NotFound();
        return View(item);
    }

    /// <summary>
    /// Akşam Kasa detayı.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> AksamKasaDetail(Guid id, CancellationToken ct)
    {
        var item = await _legacy.GetAksamKasaByIdAsync(id, ct);
        if (item is null) return NotFound();
        return View(item);
    }

    /// <summary>
    /// Genel Kasa Rapor detayı.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GenelKasaDetail(Guid id, CancellationToken ct)
    {
        var item = await _legacy.GetGenelKasaByIdAsync(id, ct);
        if (item is null) return NotFound();
        return View(item);
    }
}

/// <summary>
/// Legacy Index sayfası ViewModel'i.
/// </summary>
public sealed class LegacyIndexViewModel
{
    public required string Tab { get; init; }
    public LegacyPagedResult<KasaManager.Domain.Legacy.LegacySabahKasa>? SabahResult { get; init; }
    public LegacyPagedResult<KasaManager.Domain.Legacy.LegacyAksamKasa>? AksamResult { get; init; }
    public LegacyPagedResult<KasaManager.Domain.Legacy.LegacyGenelKasaRapor>? GenelResult { get; init; }
}
