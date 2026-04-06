using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Domain.Calculation.Data;
using KasaManager.Web.Models;
using KasaManager.Application.Services.DataFirst;

namespace KasaManager.Web.Controllers;

[Authorize(Roles = "Admin")]
public class DataPilotController : Controller
{
    private readonly KasaManagerDbContext _dbContext;
    private readonly IDataFirstTrustService _trustService;
    private readonly KasaManager.Application.Services.ReadAdapter.IDualKasaReadService _dualReadService;
    private readonly ISwitchSimulationService _switchSimulationService;
    private readonly ISwitchReadinessPolicyService _policyService;
    private readonly ISwitchGateService _gateService;
    private readonly IManualSwitchOrchestrator _orchestrator;

    public DataPilotController(
        KasaManagerDbContext dbContext, 
        IDataFirstTrustService trustService,
        KasaManager.Application.Services.ReadAdapter.IDualKasaReadService dualReadService,
        ISwitchSimulationService switchSimulationService,
        ISwitchReadinessPolicyService policyService,
        ISwitchGateService gateService,
        IManualSwitchOrchestrator orchestrator)
    {
        _dbContext = dbContext;
        _trustService = trustService;
        _dualReadService = dualReadService;
        _switchSimulationService = switchSimulationService;
        _policyService = policyService;
        _gateService = gateService;
        _orchestrator = orchestrator;
    }

    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? startDate, DateOnly? endDate, string? kasaType, bool? isEligibleFilter, CancellationToken ct)
    {
        // View Model Setup
        var vm = new DataPilotIndexViewModel
        {
            StartDate = startDate,
            EndDate = endDate,
            KasaType = kasaType,
            IsEligibleFilter = isEligibleFilter
        };

        // Get the last daily calculation results (we need all history to get the latest version of each date/scope)
        var resultsQuery = await _dbContext.DailyCalculationResults
            .OrderByDescending(r => r.ForDate)
            .ToListAsync(ct);

        var latestResults = resultsQuery
            .GroupBy(r => new { r.ForDate, r.KasaTuru })
            .Select(g => g.OrderByDescending(x => x.CalculatedVersion).First())
            .ToList();

        // Optional Memory Filtering
        if (startDate.HasValue) latestResults = latestResults.Where(r => r.ForDate >= startDate.Value).ToList();
        if (endDate.HasValue) latestResults = latestResults.Where(r => r.ForDate <= endDate.Value).ToList();
        if (!string.IsNullOrEmpty(kasaType)) latestResults = latestResults.Where(r => r.KasaTuru == kasaType).ToList();

        // Calculate KPI boundaries
        var minDate = latestResults.Any() ? latestResults.Min(r => r.ForDate) : DateOnly.MinValue;
        var maxDate = latestResults.Any() ? latestResults.Max(r => r.ForDate) : DateOnly.MaxValue;

        var allDrifts = await _dbContext.CalculationParityDrifts
            .Where(d => d.TargetDate >= minDate && d.TargetDate <= maxDate)
            .ToListAsync(ct);
            
        var allTrustSnapshots = await _dbContext.DataFirstTrustSnapshots
            .Where(s => s.TargetDate >= minDate && s.TargetDate <= maxDate)
            .ToListAsync(ct);

        // Phase 8 & 9: Fetch Dual Kasa Results and Switch Simulation using bulk optimization
        var scopePairs = latestResults.Select(r => (r.ForDate, r.KasaTuru)).Distinct();
        var dualResults = await _dualReadService.GetDualResultsBulkAsync(scopePairs, ct);
        var simulationResults = await _switchSimulationService.SimulateBulkAsync(scopePairs, ct);

        // Phase 10: Map bulk inputs and evaluate policy without side calculations
        var policyInputs = dualResults.Select(d => new PolicyInput
        {
            TargetDate = d.TargetDate,
            KasaType = d.KasaType,
            TrustScore = (int)d.TrustScore, // Assuming mapped or safely castable
            DriftAmount = d.DifferenceAmount,
            IsDataComplete = true // Since DualResult exists, data is complete
        }).ToList();
        
        var policyResults = await _policyService.EvaluateBulkAsync(policyInputs, ct);

        // Phase 11: Switch Gate Evaluation (PURE)
        var gateResults = await _gateService.EvaluateBulkAsync(policyResults, ct);

        // Aggregate Grid Data
        var gridItems = new List<ReadinessGridItem>();
        foreach (var r in latestResults)
        {
            var dayDrifts = allDrifts.Where(d => d.TargetDate == r.ForDate && d.KasaScope == r.KasaTuru).ToList();
            var trustRecord = allTrustSnapshots.FirstOrDefault(s => s.TargetDate == r.ForDate && s.KasaType == r.KasaTuru);
            
            if (trustRecord == null)
            {
                // Fallback to in-memory calculation WITHOUT saving
                trustRecord = await _trustService.CalculateAsync(r.ForDate, r.KasaTuru, ct);
            }
            
            var hasMajorMinor = dayDrifts.Any(d => d.Severity == DriftSeverity.MajorDrift || d.Severity == DriftSeverity.MinorDrift);
            var openDrifts = dayDrifts.Where(d => 
                (d.Severity == DriftSeverity.MajorDrift || d.Severity == DriftSeverity.MinorDrift) &&
                (d.ResolutionStatus == DriftResolutionStatus.Open || d.ResolutionStatus == DriftResolutionStatus.Investigating)
            ).ToList();

            var exactUnresolved = dayDrifts.Count(d => d.Severity == DriftSeverity.ExactMatch && 
                  (d.ResolutionStatus == DriftResolutionStatus.Open || d.ResolutionStatus == DriftResolutionStatus.Investigating));

            var isEligible = !r.IsStale && !r.IsLocked && !openDrifts.Any();
            var blockReason = r.IsLocked ? "LockedPeriod" : 
                              r.IsStale ? "StaleResult" : 
                              openDrifts.Any() ? "NotMatchedParity" : "Eligible";
            var readiness = isEligible ? PilotReadiness.Ready : PilotReadiness.Blocked;

            var dualRes = dualResults.FirstOrDefault(d => d.TargetDate == r.ForDate && d.KasaType == r.KasaTuru);
            var simRes = simulationResults.FirstOrDefault(d => d.TargetDate == r.ForDate && d.KasaType == r.KasaTuru);
            var polRes = policyResults.FirstOrDefault(d => d.TargetDate == r.ForDate && d.KasaType == r.KasaTuru);
            var gateRes = gateResults.FirstOrDefault(d => d.TargetDate == r.ForDate && d.PolicyStatus == polRes?.Status && d.TrustScore == polRes?.TrustScore);

            gridItems.Add(new ReadinessGridItem
            {
                TargetDate = r.ForDate,
                KasaScope = r.KasaTuru,
                ReadinessStatus = readiness,
                BlockReason = blockReason,
                OpenDriftsCount = openDrifts.Count,
                ExactUnresolvedCount = exactUnresolved,
                TrustSnapshot = trustRecord,
                DualResult = dualRes,
                SimulationResult = simRes,
                PolicyResult = polRes,
                GateResult = gateRes,
                IsEligible = isEligible,
                IsStale = r.IsStale,
                IsLocked = r.IsLocked,
                Drifts = dayDrifts.Where(d => d.Severity == DriftSeverity.MajorDrift || d.Severity == DriftSeverity.MinorDrift || d.Severity == DriftSeverity.ExactMatch).OrderByDescending(x => x.Severity).ToList()
            });
        }

        // Apply IsEligible Filter if requested
        if (isEligibleFilter.HasValue)
        {
            gridItems = gridItems.Where(g => g.IsEligible == isEligibleFilter.Value).ToList();
        }

        vm.GridItems = gridItems;

        // Populate KPIs explicitly using the db data projection/memory group
        vm.TotalDaysAnalyzed = gridItems.Count;
        vm.EligibleDays = gridItems.Count(g => g.IsEligible);
        vm.NotEligibleDays = gridItems.Count(g => !g.IsEligible);

        // Drift Level metrics
        var matchingDrifts = allDrifts.Where(d => gridItems.Any(g => g.TargetDate == d.TargetDate && g.KasaScope == d.KasaScope)).ToList();
        vm.OpenDrifts = matchingDrifts.Count(d => d.ResolutionStatus == DriftResolutionStatus.Open);
        vm.InvestigatingDrifts = matchingDrifts.Count(d => d.ResolutionStatus == DriftResolutionStatus.Investigating);
        vm.ResolvedDrifts = matchingDrifts.Count(d => d.ResolutionStatus == DriftResolutionStatus.Resolved);
        vm.AcceptedDifference = matchingDrifts.Count(d => d.ResolutionStatus == DriftResolutionStatus.AcceptedDifference);
        vm.FalsePositive = matchingDrifts.Count(d => d.ResolutionStatus == DriftResolutionStatus.FalsePositive);

        // Generate Info Summary
        vm.PilotSummaryHtml = $"Toplam <strong>{vm.TotalDaysAnalyzed}</strong> geçerli günden <strong>{vm.EligibleDays}</strong> adet gün pilot gösterimi için 'Ready' (Hazır) durumdadır.";

        // Populate Recent Trend (last 7 snapshots globally)
        vm.RecentTrustTrend = allTrustSnapshots
            .OrderByDescending(s => s.TargetDate)
            .Take(7)
            .ToList();

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveDrift(Guid driftId, DriftResolutionStatus status, string? note, CancellationToken ct)
    {
        var safeNote = note?.Trim();

        // Kural 1: ResolutionNote kontrolü
        if (string.IsNullOrWhiteSpace(safeNote))
        {
            TempData["Error"] = "Açıklama (Resolution Note) girmeden durum değişikliği yapılamaz.";
            return RedirectToAction(nameof(Index));
        }

        // Kural 3: Drift bulunamazsa güvenli exit
        var drift = await _dbContext.CalculationParityDrifts.FindAsync(new object[] { driftId }, ct);
        if (drift == null)
        {
            TempData["Warning"] = "İlgili drift kaydı bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        // Kural 4: Zaten aynı status ise tekrar yazma (idempotent)
        if (drift.ResolutionStatus == status && drift.ResolutionNote == safeNote)
        {
            TempData["Info"] = "Drift durumu zaten aynı, değişiklik kaydedilmedi.";
            return RedirectToAction(nameof(Index));
        }

        // Başarılı durumda atamaları yap
        drift.ResolutionStatus = status;
        drift.ResolutionNote = safeNote;
        drift.ReviewedBy = User.Identity?.Name ?? "SystemAdmin";
        drift.ReviewedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        
        TempData["Success"] = "Drift başarıyla güncellendi.";

        return RedirectToAction(nameof(Index));
    }

    // Faz 12: Manual Switch Check Endpoint
    [HttpGet("DataPilot/ManualSwitchCheck")]
    public async Task<IActionResult> ManualSwitchCheck([FromQuery] string dateStr, [FromQuery] string kasaType, CancellationToken ct)
    {
        if (!DateOnly.TryParse(dateStr, out var date))
            return BadRequest("Invalid date format.");

        var scopes = new[] { (date, kasaType) };
        var dualResults = await _dualReadService.GetDualResultsBulkAsync(scopes, ct);
        var dual = dualResults.FirstOrDefault();
        if (dual == null) return NotFound(new { error = "DataFirst Result could not be found for given parameters." });

        var policyInputs = new List<PolicyInput> {
            new PolicyInput {
                TargetDate = dual.TargetDate,
                KasaType = dual.KasaType,
                TrustScore = (int)dual.TrustScore,
                DriftAmount = dual.DifferenceAmount,
                IsDataComplete = true
            }
        };

        var policyRes = (await _policyService.EvaluateBulkAsync(policyInputs, ct)).FirstOrDefault();
        if (policyRes == null) return StatusCode(500, "Policy evaluation failed");

        var gateRes = (await _gateService.EvaluateBulkAsync(new List<PolicyResult> { policyRes }, ct)).FirstOrDefault();
        if (gateRes == null) return StatusCode(500, "Gate evaluation failed");

        var orchestratorResult = await _orchestrator.EvaluateAsync(
            policyRes, 
            gateRes, 
            dual.LegacyResult, 
            dual.DataFirstResult, 
            ct);

        // Audit Log simulation (Phase 12 optional recommendation)
        await Task.CompletedTask; // Emulating log write without DB interaction

        return Json(new { success = true, result = orchestratorResult });
    }
}
