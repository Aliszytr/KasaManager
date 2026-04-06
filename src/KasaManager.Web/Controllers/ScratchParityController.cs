using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace KasaManager.Web.Controllers;

#if DEBUG
[ApiController]
[Route("api/[controller]")]
public class ScratchParityController : ControllerBase
{
    private readonly IKasaDraftService _draftService;
    private readonly IConfiguration _cfg;

    public ScratchParityController(IKasaDraftService draftService, IConfiguration cfg)
    {
        _draftService = draftService;
        _cfg = cfg;
    }

    [HttpGet("run-parity")]
    public async Task<IActionResult> RunParity()
    {
        try
        {
            // P1(C): USE_LIVE_USTRAPOR_SOURCE artık IOptions<UstRaporSourceOptions> üzerinden okunuyor.
            // appsettings.json: "UstRaporSource": { "UseLiveSource": true }

            var sub = _cfg.GetValue<string>("Upload:SubFolder") ?? "Data\\Raporlar";
            var envPath = Directory.GetCurrentDirectory(); 
            var uploadFolderAbsolute = Path.Combine(envPath, "wwwroot", sub);

            var datesToTest = new[] 
            {
                new DateOnly(2026, 3, 28),
                new DateOnly(2026, 3, 29),
                new DateOnly(2026, 3, 30),
                new DateOnly(2026, 3, 31),
                new DateOnly(2026, 4, 2)
            };

            var results = new List<string>();

            foreach (var date in datesToTest)
            {
                try
                {
                    var draftRes = await _draftService.BuildAsync(date, uploadFolderAbsolute, null, CancellationToken.None);
                    if (draftRes.Ok)
                    {
                        results.Add($"{date:dd.MM.yyyy}: SUCCESS (BuildAsync ok)");
                    }
                    else
                    {
                        results.Add($"{date:dd.MM.yyyy}: FAIL - {draftRes.Error}");
                    }
                }
                catch(Exception ex)
                {
                    results.Add($"{date:dd.MM.yyyy}: ERROR - {ex.Message}");
                }
            }

            return Ok(new { ConfigNote = "UstRaporSource.UseLiveSource is now config-driven (appsettings)", Results = results });
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }
}
#endif
