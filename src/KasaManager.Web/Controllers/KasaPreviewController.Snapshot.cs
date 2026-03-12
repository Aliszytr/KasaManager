using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Web.Helpers;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace KasaManager.Web.Controllers;

// ─────────────────────────────────────────────────────────────
// KasaPreviewController — Snapshot CRUD (Kaydet / Yükle / Güncelle / Sil)
// ─────────────────────────────────────────────────────────────
public sealed partial class KasaPreviewController
{
    /// <summary>
    /// KasaPreview'daki hesaplama sonuçlarını DB'ye CalculatedKasaSnapshot olarak kaydeder.
    /// AJAX uyumlu: İlk çağrıda mevcut rapor varsa onay ister (JSON),
    /// ConfirmOverwrite=true ile tekrar çağrılırsa versiyonlayarak kaydeder.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveReport(KasaPreviewViewModel model, CancellationToken ct)
    {
        try
        {
            var raporAdi = Request.Form["SaveRaporAdi"].ToString().Trim();
            var raporNot = Request.Form["RptGunlukNot"].ToString().Trim();
            var inputsJson = Request.Form["SaveInputsJson"].ToString();
            var outputsJson = Request.Form["SaveOutputsJson"].ToString();
            var confirmOverwrite = Request.Form["ConfirmOverwrite"].ToString()
                .Equals("true", StringComparison.OrdinalIgnoreCase);

            var effectiveKasaType = !string.IsNullOrEmpty(model.KasaType) ? model.KasaType : "Aksam";
            var tarih = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today);

            // KasaRaporData oluştur ve serialize et
            var kasaRaporData = await BuildKasaRaporDataAsync(model, includeUstRapor: true, ct);
            var kasaRaporDataJson = JsonSerializer.Serialize(kasaRaporData, new JsonSerializerOptions { WriteIndented = false });

            // Auto-generate name if empty
            if (string.IsNullOrWhiteSpace(raporAdi))
                raporAdi = $"{effectiveKasaType} Kasa — {tarih:dd.MM.yyyy}";

            // KasaTuru enum mapping
            var kasaTuruEnum = effectiveKasaType.ToLowerInvariant() switch
            {
                "sabah" => KasaRaporTuru.Sabah,
                "aksam" or "akşam" => KasaRaporTuru.Aksam,
                "genel" => KasaRaporTuru.Genel,
                _ => KasaRaporTuru.Ortak
            };

            // ── Akıllı Kaydetme: Mevcut rapor kontrolü ──
            var existingActive = await _calcSnapshots.GetActiveAsync(tarih, kasaTuruEnum, ct);
            if (existingActive != null && !confirmOverwrite)
            {
                return Json(new
                {
                    ok = false, needsConfirmation = true,
                    message = $"Bu tarihli {effectiveKasaType} Kasa raporu zaten kayıtlı.",
                    existingVersion = existingActive.Version,
                    existingName = existingActive.Name ?? raporAdi,
                    tarih = tarih.ToString("dd.MM.yyyy")
                });
            }

            var snapshot = new CalculatedKasaSnapshot
            {
                RaporTarihi = tarih, KasaTuru = kasaTuruEnum,
                Name = raporAdi, Notes = raporNot,
                CalculatedBy = model.KasayiYapan ?? "Sistem",
                InputsJson = !string.IsNullOrWhiteSpace(inputsJson) ? inputsJson : "{}",
                OutputsJson = !string.IsNullOrWhiteSpace(outputsJson) ? outputsJson : "{}",
                KasaRaporDataJson = kasaRaporDataJson,
                FormulaSetName = model.FormulaRun?.FormulaSetId
            };

            if (!string.IsNullOrEmpty(model.DbFormulaSetId) && Guid.TryParse(model.DbFormulaSetId, out var fsGuid))
                snapshot.FormulaSetId = fsGuid;

            // Faz 3: Snapshot'a Financial Exceptions özet verisi enjekte et
            try
            {
                var istisnalar = await _finansalIstisna.ListByDateAsync(tarih, ct);
                if (istisnalar.Count > 0)
                {
                    var feSummary = FinancialExceptionsSummary.Build(istisnalar);
                    snapshot.FinancialExceptionsSummaryJson = feSummary.ToJson();
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Snapshot'a Financial Exceptions summary eklenemedi");
            }

            await _calcSnapshots.SaveAsync(snapshot, ct);

            // Draft cache temizle — veriler artık DB'de
            try
            {
                var saveUserName = User.Identity?.Name ?? "anonymous";
                await KasaDraftCacheHelper.ClearDraftAsync(saveUserName, effectiveKasaType);
            }
            catch (Exception ex) { _log.LogDebug(ex, "Draft cache temizleme başarısız (rapor kaydı etkilenmedi)"); }

            var isUpdate = existingActive != null;
            var actionWord = isUpdate ? "güncellendi" : "kaydedildi";

            _log.LogInformation("Rapor {Action}: {Name}, Tarih={Tarih}, Tip={Tip}, v{Version}, Id={Id}",
                actionWord, snapshot.Name, snapshot.RaporTarihi, snapshot.KasaTuru, snapshot.Version, snapshot.Id);

            return Json(new
            {
                ok = true,
                message = isUpdate
                    ? $"✅ {tarih:dd.MM.yyyy} tarihli rapor güncellendi → v{snapshot.Version}"
                    : $"✅ Rapor başarıyla kaydedildi: {snapshot.Name} (v{snapshot.Version})",
                redirectUrl = Url.Action("LoadSnapshot", new { id = snapshot.Id }),
                version = snapshot.Version, isUpdate
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Rapor kaydetme hatasi");
            return Json(new { ok = false, needsConfirmation = false, message = $"❌ Rapor kaydedilemedi: {ex.Message}" });
        }
    }

    /// <summary>
    /// AJAX: Mevcut kasa tipine ait raporları JSON olarak döner.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SearchReports(string? kasaType, string? searchDate, string? search, CancellationToken ct)
    {
        KasaRaporTuru? turu = kasaType?.ToLowerInvariant() switch
        {
            "sabah" => KasaRaporTuru.Sabah,
            "aksam" or "akşam" => KasaRaporTuru.Aksam,
            "genel" => KasaRaporTuru.Genel,
            "ortak" => KasaRaporTuru.Ortak,
            _ => null
        };

        DateOnly? filterDate = null;
        if (!string.IsNullOrWhiteSpace(searchDate))
        {
            if (DateOnly.TryParseExact(searchDate, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d1))
                filterDate = d1;
            else if (DateOnly.TryParseExact(searchDate, "dd.MM.yyyy", null, System.Globalization.DateTimeStyles.None, out var d2))
                filterDate = d2;
        }

        var query = new KasaReportSearchQuery
        {
            KasaTuru = turu, SearchText = search,
            StartDate = filterDate, EndDate = filterDate,
            IncludeDeleted = false, SortBy = "RaporTarihi",
            SortDescending = true, Page = 1, PageSize = 50
        };

        var results = await _calcSnapshots.SearchAsync(query, ct);

        var items = results.Items.Select(s => new
        {
            s.Id, s.Name, s.Notes,
            RaporTarihi = s.RaporTarihi.ToString("dd.MM.yyyy"),
            KasaTuru = s.KasaTuru.ToString(), s.CalculatedBy,
            CalculatedAt = s.CalculatedAtUtc.ToLocalTime().ToString("dd.MM HH:mm"),
            s.Version, s.IsActive
        });

        return Json(new { items, results.TotalCount });
    }

    /// <summary>
    /// Kaydedilmiş snapshot'ı KasaPreview'a re-hydrate ederek yükler.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> LoadSnapshot(Guid id, CancellationToken ct)
    {
        var snapshot = await _calcSnapshots.GetByIdAsync(id, ct);
        if (snapshot is null)
        {
            TempData["ErrorMessage"] = "❌ Rapor bulunamadı.";
            return RedirectToAction("Index");
        }

        // KasaRaporData'yı deserialize et
        KasaRaporData? raporData = null;
        if (!string.IsNullOrWhiteSpace(snapshot.KasaRaporDataJson))
        {
            try
            {
                raporData = JsonSerializer.Deserialize<KasaRaporData>(
                    snapshot.KasaRaporDataJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "KasaRaporDataJson deserialize edilemedi - Id={Id}", id);
            }
        }

        var kasaTypeString = snapshot.KasaTuru switch
        {
            KasaRaporTuru.Sabah => "Sabah",
            KasaRaporTuru.Aksam => "Aksam",
            KasaRaporTuru.Genel => "Genel",
            _ => "Ortak"
        };

        var model = new KasaPreviewViewModel
        {
            SelectedDate = snapshot.RaporTarihi, KasaType = kasaTypeString,
            KasayiYapan = snapshot.CalculatedBy, GunlukKasaNotu = snapshot.Notes,
            HasResults = true, IsDataLoaded = true,
            LoadedSnapshotId = snapshot.Id, LoadedSnapshotName = snapshot.Name,
            LoadedSnapshotVersion = snapshot.Version,
        };

        // FormulaRun inputs/outputs re-hydrate
        if (raporData != null)
        {
            var inputs = snapshot.GetInputs();
            var outputs = snapshot.GetOutputs();

            // ── KasaRaporData → FormulaRun.Outputs enjeksiyonu ──
            // KasaRaporData kaydedilen snapshot'ın "ground truth"udur.
            // OutputsJson formül motoru çıktılarını içerebilir ama Eksik/Fazla,
            // VergideBiriken gibi HesapKontrol/Ledger kaynaklı alanlar orada yoktur.
            // Bu yüzden KasaRaporData'dan HER ZAMAN enjekte ediyoruz.

            // Temel formül çıktıları — OutputsJson boşsa sentezle
            outputs.TryAdd("dunden_devreden_kasa_nakit", raporData.DundenDevredenKasa);
            outputs.TryAdd("genel_kasa", raporData.GenelKasa);
            outputs.TryAdd("online_reddiyat", raporData.OnlineReddiyat);
            outputs.TryAdd("bankadan_cikan_tahsilat", raporData.BankadanCikan);
            outputs.TryAdd("toplam_stopaj", raporData.ToplamStopaj);
            outputs.TryAdd("stopaj_kontrol", raporData.StopajKontrolFark);
            outputs.TryAdd("bankaya_yatirilacak_tahsilat", raporData.BankayaTahsilat);
            outputs.TryAdd("bankaya_yatirilacak_harc", raporData.BankayaHarc);
            outputs.TryAdd("bankaya_yatirilacak_toplam", raporData.BankayaToplam);
            outputs.TryAdd("kasadaki_nakit", raporData.KasadakiNakit);
            outputs.TryAdd("banka_devreden_tahsilat", raporData.DundenDevredenBanka);
            outputs.TryAdd("banka_yarina_devredecek_tahsilat", raporData.YarinaDevredecekBanka);
            outputs.TryAdd("vergiden_gelen", raporData.VergidenGelen);
            outputs.TryAdd("eft_otomatik_iade", raporData.EftOtomatikIade);
            outputs.TryAdd("gelen_havale", raporData.GelenHavale);
            outputs.TryAdd("iade_kelimesi_giris", raporData.IadeKelimesiGiris);

            // Eksik/Fazla alanları — HesapKontrol modülünden gelir, formül motorunda yok.
            // HER ZAMAN KasaRaporData'dan overwrite et (ground truth).
            outputs["gune_ait_eksik_fazla_tahsilat"] = raporData.GuneAitEksikFazlaTahsilat;
            outputs["dunden_eksik_fazla_tahsilat"] = raporData.DundenEksikFazlaTahsilat;
            outputs["dunden_eksik_fazla_gelen_tahsilat"] = raporData.DundenEksikFazlaGelenTahsilat;
            outputs["gune_ait_eksik_fazla_harc"] = raporData.GuneAitEksikFazlaHarc;
            outputs["dunden_eksik_fazla_harc"] = raporData.DundenEksikFazlaHarc;
            outputs["dunden_eksik_fazla_gelen_harc"] = raporData.DundenEksikFazlaGelenHarc;

            // Banka doğrulama alanları — her durumda KasaRaporData'dan enjekte et
            outputs["banka_mevduat_tahsilat"] = raporData.BankaMevduatTahsilat;
            outputs["banka_virman_tahsilat"] = raporData.BankaVirmanTahsilat;
            outputs["banka_mevduat_harc"] = raporData.BankaMevduatHarc;

            // Banka reconciliation alanları
            outputs.TryAdd("bankaya_giren_tahsilat", raporData.BankaGirenTahsilat);
            outputs.TryAdd("bankaya_giren_harc", raporData.BankaGirenHarc);
            outputs.TryAdd("online_tahsilat", raporData.OnlineTahsilat);
            outputs.TryAdd("online_harc", raporData.OnlineHarc);

            model.FormulaRun = new KasaManager.Domain.Calculation.CalculationRun
            {
                ReportDate = snapshot.RaporTarihi,
                FormulaSetId = snapshot.FormulaSetId?.ToString() ?? "",
                FormulaSetVersion = snapshot.FormulaSetName ?? "",
                Inputs = inputs, Outputs = outputs
            };

            // Eksik/Fazla alanları (re-hydration from KasaRaporData)
            model.GuneAitEksikFazlaTahsilat = raporData.GuneAitEksikFazlaTahsilat;
            model.DundenEksikFazlaTahsilat = raporData.DundenEksikFazlaTahsilat;
            model.DundenEksikFazlaGelenTahsilat = raporData.DundenEksikFazlaGelenTahsilat;
            model.GuneAitEksikFazlaHarc = raporData.GuneAitEksikFazlaHarc;
            model.DundenEksikFazlaHarc = raporData.DundenEksikFazlaHarc;
            model.DundenEksikFazlaGelenHarc = raporData.DundenEksikFazlaGelenHarc;

            // Vergi alanları
            model.VergiKasaBakiyeToplam = raporData.VergiKasa;
            model.VergideBirikenKasa = raporData.VergideBirikenKasa;
            model.VergiKasaVeznedarlar = raporData.VergiCalisanlari;
        }

        // Panel + IBAN  hydration
        model.UstRaporPanel = await HydrateUstRaporPanelAsync(ct);
        model.LastSnapshotDate = snapshot.RaporTarihi;
        model.HasUploadedFiles = ListUploadedFiles().Count > 0;
        await HydrateIbanInfoAsync(model, ct);

        // Validation: Kaydedilmiş KasaRaporData'dan doğrudan çalıştır
        if (raporData != null)
        {
            try
            {
                model.ValidationResults = _validation.Validate(raporData);
                model.DismissedRuleCodes = await _validation.GetDismissedCodesAsync(
                    snapshot.RaporTarihi, kasaTypeString, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "LoadSnapshot validation çalıştırılamadı - Id={Id}", id);
            }
        }

        // DB FormulaSet dropdown
        var dto = model.ToDto();
        await _orchestrator.HydrateDbFormulaSetsAsync(dto, ct);
        model.UpdateFromDto(dto);

        TempData["SuccessMessage"] = $"📂 Rapor yüklendi: {snapshot.Name} (v{snapshot.Version})";
        return View("Index", model);
    }

    /// <summary>
    /// AJAX POST: Yüklü snapshot'ın adını ve notlarını günceller.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSnapshot(
        [FromForm] Guid snapshotId,
        [FromForm] string? name,
        [FromForm] string? notes,
        CancellationToken ct)
    {
        try
        {
            var snapshot = await _calcSnapshots.GetByIdAsync(snapshotId, ct);
            if (snapshot is null)
                return Json(new { ok = false, message = "Rapor bulunamadı." });

            await _calcSnapshots.UpdateAsync(snapshotId, name?.Trim(), null, notes?.Trim(), ct);

            return Json(new { ok = true, message = $"✅ Rapor güncellendi: {name?.Trim() ?? snapshot.Name}" });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Snapshot güncelleme hatası - Id={Id}", snapshotId);
            return Json(new { ok = false, message = $"❌ Hata: {ex.Message}" });
        }
    }

    /// <summary>
    /// AJAX POST: Yüklü snapshot'ı soft-delete eder.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSnapshot(
        [FromForm] Guid snapshotId,
        CancellationToken ct)
    {
        try
        {
            var snapshot = await _calcSnapshots.GetByIdAsync(snapshotId, ct);
            if (snapshot is null)
                return Json(new { ok = false, message = "Rapor bulunamadı." });

            await _calcSnapshots.DeleteAsync(snapshotId, null, ct);

            return Json(new { ok = true, message = $"🗑️ Rapor silindi: {snapshot.Name}" });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Snapshot silme hatası - Id={Id}", snapshotId);
            return Json(new { ok = false, message = $"❌ Hata: {ex.Message}" });
        }
    }
}
