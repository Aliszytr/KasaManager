using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Validation;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace KasaManager.Web.Controllers;

// ─────────────────────────────────────────────────────────────
// KasaPreviewController — Helpers (BuildData / Hydration / Validation)
// ─────────────────────────────────────────────────────────────
public sealed partial class KasaPreviewController
{
    /// <summary>
    /// Hidden input'lardan posted değerleri okuyarak KasaRaporData oluşturur.
    /// Engine tekrar çalıştırılmaz — ekrandaki değerler birebir kullanılır.
    /// </summary>
    private async Task<KasaManager.Domain.Reports.KasaRaporData> BuildKasaRaporDataAsync(
        KasaPreviewViewModel model, bool includeUstRapor, CancellationToken ct)
    {
        decimal PF(string name)
            => decimal.TryParse(Request.Form[name], System.Globalization.NumberStyles.Any,
                   System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

        var defaults = await _globalDefaults.GetAsync(ct);
        var effectiveKasaType = !string.IsNullOrEmpty(model.KasaType) ? model.KasaType : "Aksam";
        var tarih = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today);
        var isSabah = effectiveKasaType.Equals("Sabah", StringComparison.OrdinalIgnoreCase);

        var data = new KasaManager.Domain.Reports.KasaRaporData
        {
            Tarih = tarih, KasaTuru = effectiveKasaType,
            KasayiYapan = model.KasayiYapan,
            GunlukNot = Request.Form["RptGunlukNot"].ToString(),
            // ROW 1
            DundenDevredenKasa = PF("RptDundenDevreden"), GenelKasa = PF("RptGenelKasa"),
            // ROW 2
            OnlineReddiyat = PF("RptOnlineReddiyat"), BankadanCikan = PF("RptBankadanCikan"),
            BankadanCekilen = PF("RptBankadanCekilen"), ToplamStopaj = PF("RptToplamStopaj"),
            StopajKontrolOk = Request.Form["RptStopajOk"] == "true", StopajKontrolFark = PF("RptStopajFark"),
            // ROW 3 — Bankaya Götürülecek
            BankayaStopaj = PF("RptBankayaStopaj"), BankayaTahsilat = PF("RptBankayaTahsilat"), BankayaHarc = PF("RptBankayaHarc"),
            // IBAN
            HesapAdiStopaj = defaults.HesapAdiStopaj, IbanStopaj = defaults.IbanStopaj,
            HesapAdiTahsilat = defaults.HesapAdiMasraf, IbanTahsilat = defaults.IbanMasraf,
            HesapAdiHarc = defaults.HesapAdiHarc, IbanHarc = defaults.IbanHarc,
            // Devir
            KasadakiNakit = PF("RptKasadakiNakit"), DundenDevredenBanka = PF("RptDevredenBanka"),
            YarinaDevredecekBanka = PF("RptDevredecekBanka"),
            // Vergi
            VergidenGelen = PF("RptVergidenGelen"), VergiKasa = PF("RptVergiKasa"), VergideBirikenKasa = PF("RptVergideBiriken"),
            // Beklenen Girişler
            EftOtomatikIade = PF("RptEftIade"), GelenHavale = PF("RptGelenHavale"), IadeKelimesiGiris = PF("RptIadeKelimesi"),
            // Banka Reconciliation
            BankaGirenTahsilat = PF("RptBankaGirenTahsilat"), BankaGirenHarc = PF("RptBankaGirenHarc"),
            OnlineTahsilat = PF("RptOnlineTahsilat"), OnlineHarc = PF("RptOnlineHarc"),
            // Eksik/Fazla
            IsSabahKasa = isSabah,
            GuneAitEksikFazlaTahsilat = PF("RptEfGuneT"), DundenEksikFazlaTahsilat = PF("RptEfDundenT"),
            DundenEksikFazlaGelenTahsilat = PF("RptEfGelenT"), GuneAitEksikFazlaHarc = PF("RptEfGuneH"),
            DundenEksikFazlaHarc = PF("RptEfDundenH"), DundenEksikFazlaGelenHarc = PF("RptEfGelenH"),
            // ROW 4D: Bankaya Yatırılacak Doğrulama (Sabah Kasa)
            BankaMevduatTahsilat = PF("RptBankaMevduatTahsilat"),
            BankaVirmanTahsilat = PF("RptBankaVirmanTahsilat"), BankaMevduatHarc = PF("RptBankaMevduatHarc"),
        };

        // BankayaToplam backend'de hesaplanır
        data.BankayaToplam = data.BankayaStopaj + data.BankayaTahsilat + data.BankayaHarc;

        var vc = Request.Form["RptVergiCalisanlari"].ToString();
        if (!string.IsNullOrEmpty(vc))
            data.VergiCalisanlari = vc.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        // Üst Rapor tablosu
        if (includeUstRapor)
        {
            try
            {
                var ustRaporPanel = await HydrateUstRaporPanelAsync(ct);
                if (ustRaporPanel?.Table != null)
                {
                    var vezCol = ustRaporPanel.VeznedarColumn ?? "VEZNEDAR";
                    data.UstRaporKolonlar = ustRaporPanel.Table.Columns
                        .Where(c => !c.Equals(vezCol, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var row in ustRaporPanel.Table.Rows)
                    {
                        row.TryGetValue(vezCol, out var vezAdi);
                        data.UstRaporSatirlar.Add(new KasaManager.Domain.Reports.UstRaporSatir
                        {
                            VeznedarAdi = vezAdi ?? "",
                            Degerler = new Dictionary<string, string?>(row)
                        });
                    }
                }
            }
            catch (Exception ex) { _log.LogDebug(ex, "ÜstRapor panel verisi alınamazsa rapor yine üretilir, tablo boş kalır"); }
        }

        return data;
    }

    // =========================================================================
    // Private Helpers
    // =========================================================================

    /// <summary>
    /// B6: HesapKontrol'den Sabah Kasa eksik/fazla alanlarını otomatik doldurur.
    /// </summary>
    private async Task TryAutoFillEksikFazlaAsync(KasaPreviewViewModel model, CancellationToken ct)
    {
        try
        {
            var analizTarihi = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Now);
            var fill = await _hesapKontrol.GetAutoFillDataAsync(analizTarihi, ct);
            model.HesapKontrolAutoFillMessage = fill.InfoMessage;
            if (fill.HasData)
            {
                model.GuneAitEksikFazlaTahsilat = fill.GuneAitEksikFazlaTahsilat;
                model.GuneAitEksikFazlaHarc = fill.GuneAitEksikFazlaHarc;
                model.DundenEksikFazlaTahsilat = fill.OncekiGunAcikTahsilat;
                model.DundenEksikFazlaHarc = fill.OncekiGunAcikHarc;
                model.DundenEksikFazlaGelenTahsilat = fill.BugunCozulenTahsilat;
                model.DundenEksikFazlaGelenHarc = fill.BugunCozulenHarc;
            }
            // Takipte toplamları her zaman doldur
            model.TakipteEksikTahsilat = fill.TakipteEksikTahsilat;
            model.TakipteEksikHarc = fill.TakipteEksikHarc;
            model.TakipteFazlaTahsilat = fill.TakipteFazlaTahsilat;
            model.TakipteFazlaHarc = fill.TakipteFazlaHarc;
            model.TakipteSayisi = fill.TakipteSayisi;
            // Faz 1: Breakdown alanları
            model.ToplamFarkTahsilat = fill.ToplamFarkTahsilat;
            model.ToplamFarkHarc = fill.ToplamFarkHarc;
            model.BeklenenTahsilat = fill.BeklenenTahsilat;
            model.BeklenenHarc = fill.BeklenenHarc;
            model.OlaganDisiTahsilat = fill.OlaganDisiTahsilat;
            model.OlaganDisiHarc = fill.OlaganDisiHarc;
            model.BreakdownMesajTahsilat = fill.BreakdownMesajTahsilat;
            model.BreakdownMesajHarc = fill.BreakdownMesajHarc;
            // Akıllı Takip Korelasyonu
            model.TakipCozumleri = fill.TakipCozumleri;
            model.TakipCozumBildirim = fill.TakipCozumBildirim;

            // ─── CrossDay otomatik eşleştirme (kasa hesaplama sırasında tetiklenir) ───
            try
            {
                var crossDay = await _hesapKontrol.CrossDayReconcileAsync(analizTarihi, ct);
                var kesinCount = crossDay.KesirEslesmeler.Count;
                var potansiyelCount = crossDay.PotansiyelEslesmeler.Count;

                if (kesinCount > 0 || potansiyelCount > 0)
                {
                    model.CrossDayEslesmeSayisi = kesinCount;
                    model.CrossDayToplamTutar = crossDay.KesirEslesmeler.Sum(x => x.Tutar);

                    var parts = new List<string>();
                    if (kesinCount > 0)
                        parts.Add($"✅ {kesinCount} kayıt DosyaNo doğrulandı — otomatik çözüldü ({model.CrossDayToplamTutar:N2} ₺).");
                    if (potansiyelCount > 0)
                    {
                        var potTutar = crossDay.PotansiyelEslesmeler.Sum(x => x.Tutar);
                        parts.Add($"⚠️ {potansiyelCount} kısmi eşleşme onayınızı bekliyor ({potTutar:N2} ₺).");
                        model.CrossDayPotansiyelSayisi = potansiyelCount;
                    }
                    model.CrossDayBildirim = string.Join(" ", parts);
                    _log.LogInformation("CrossDay: {Kesin} kesin, {Potansiyel} potansiyel eşleşme",
                        kesinCount, potansiyelCount);

                    // CrossDay çözüldükten sonra auto-fill verilerini güncelle
                    if (kesinCount > 0)
                    {
                        var updatedFill = await _hesapKontrol.GetAutoFillDataAsync(analizTarihi, ct);
                        if (updatedFill.HasData)
                        {
                            model.DundenEksikFazlaGelenTahsilat = updatedFill.BugunCozulenTahsilat;
                            model.DundenEksikFazlaGelenHarc = updatedFill.BugunCozulenHarc;
                            model.ToplamFarkTahsilat = updatedFill.ToplamFarkTahsilat;
                            model.ToplamFarkHarc = updatedFill.ToplamFarkHarc;
                            model.TakipteSayisi = updatedFill.TakipteSayisi;
                            model.TakipteEksikTahsilat = updatedFill.TakipteEksikTahsilat;
                            model.TakipteEksikHarc = updatedFill.TakipteEksikHarc;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "CrossDay eşleştirme başarısız, kasa hesaplama etkilenmez");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "HesapKontrol auto-fill başarısız, alanlar elle doldurulmalı");
            model.HesapKontrolAutoFillMessage =
                "ℹ️ Bu bölüm Hesap Kontrol modülü çalıştırıldığında kendiliğinden dolacaktır.";
        }
    }

    /// <summary>
    /// IBAN bilgilerini global defaults'dan ViewModel'e yükler.
    /// </summary>
    private async Task HydrateIbanInfoAsync(KasaPreviewViewModel model, CancellationToken ct)
    {
        try
        {
            var defaults = await _globalDefaults.GetAsync(ct);
            model.HesapAdiStopaj = defaults.HesapAdiStopaj;
            model.IbanStopaj = defaults.IbanStopaj;
            model.HesapAdiMasraf = defaults.HesapAdiMasraf;
            model.IbanMasraf = defaults.IbanMasraf;
            model.HesapAdiHarc = defaults.HesapAdiHarc;
            model.IbanHarc = defaults.IbanHarc;
            model.IbanPostaPulu = defaults.IbanPostaPulu;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "IBAN hydration failed — kartlarda IBAN gösterilmeyecek");
        }
    }

    /// <summary>
    /// Vergide Biriken'i akıllı ledger servisinden hesaplar.
    /// </summary>
    private async Task HydrateVergideBirikenSeedAsync(KasaPreviewViewModel model, CancellationToken ct)
    {
        try
        {
            var tarih = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today);
            var kasaType = model.KasaType ?? "Aksam";
            var result = await _vergiLedger.CalculateAsync(tarih, kasaType, ct);
            model.VergideBirikenKasa = result.VergideBiriken;
            ViewData["VergideBirikenSeed"] = result.InitialSeed;
            ViewData["VergideBirikenTotalVergiKasa"] = result.TotalVergiKasa;
            ViewData["VergideBirikenTotalVergidenGelen"] = result.TotalVergidenGelen;
            ViewData["VergideBirikenSnapshotCount"] = result.SnapshotCount;
            ViewData["VergideBirikenLastDate"] = result.LastSnapshotDate;
            _log.LogInformation(
                "Vergide Biriken ledger: Seed={Seed:N2} + TVK={TVK:N2} − TVG={TVG:N2} = {VB:N2} ({Count} snapshot)",
                result.InitialSeed, result.TotalVergiKasa, result.TotalVergidenGelen,
                result.VergideBiriken, result.SnapshotCount);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Vergide Biriken ledger hesaplaması başarısız");
        }
    }

    /// <summary>
    /// Financial Exceptions: Seçili tarihin istisnalarını DB'den yükler.
    /// </summary>
    private async Task HydrateFinansalIstisnalarAsync(KasaPreviewViewModel model, CancellationToken ct)
    {
        try
        {
            var tarih = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today);
            model.FinansalIstisnalar = (await _finansalIstisna.ListByDateAsync(tarih, ct)).ToList();

            // Faz 2: Dünden devredilen istisnaları ViewBag'e yükle
            var devredilmisler = await _finansalIstisna.ListDevredilmisAsync(tarih, ct);
            if (devredilmisler.Count > 0)
                ViewBag.DevredilmisIstisnalar = devredilmisler.ToList();

            // Faz 3: Anomali analizi
            // NOT: Anomali servisi her zaman mevcut DB durumunu okur.
            // HesapKontrol diff analizi yalnızca HesapKontrol sayfasında çalışır.
            try
            {
                var anomaliler = await _anomali.AnalyzeAsync(tarih, ct);
                if (anomaliler.Count > 0)
                    model.AnomaliOnerileri = anomaliler.ToList();
            }
            catch (Exception ex2)
            {
                _log.LogWarning(ex2, "Anomali analizi başarısız");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Finansal istisna listesi yüklenemedi");
        }
    }

    private string ResolveUploadFolderAbsolute()
    {
        var sub = _cfg.GetValue<string>("Upload:SubFolder") ?? "Data\\Raporlar";
        sub = sub.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_env.WebRootPath, sub);
    }

    /// <summary>
    /// kasaType parametresini normalize eder: "aksam" → "Aksam", "sabah" → "Sabah", vb.
    /// </summary>
    private static string NormalizeKasaType(string kasaType)
    {
        return kasaType.Trim().ToLowerInvariant() switch
        {
            "aksam" or "akşam" or "aksamkasa" => "Aksam",
            "sabah" or "sabahkasa" => "Sabah",
            "genel" or "genelkasa" => "Genel",
            "ortak" or "ortakkasa" or "ozet" or "özetkasa" => "Ortak",
            _ => kasaType.Trim()
        };
    }

    // ─── KasaÜstRapor Panel Hydration ───

    private async Task<KasaUstRaporPanelViewModel?> HydrateUstRaporPanelAsync(CancellationToken ct)
    {
        try
        {
            var files = ListUploadedFiles();
            var kasaUst = PickKasaUstRaporFile(files);
            ImportedTable? table = null;
            var folder = ResolveUploadFolderAbsolute();

            if (!string.IsNullOrWhiteSpace(kasaUst))
            {
                var fullPath = Path.Combine(folder, kasaUst);
                if (System.IO.File.Exists(fullPath))
                {
                    var imported = _importOrchestrator.Import(fullPath, ImportFileKind.KasaUstRapor);
                    if (imported.Ok) table = imported.Value;
                }
            }

            var eval = await _dateRules.EvaluateAsync(folder, ct);
            var proposed = eval.ProposedDate;
            var (veznedarCol, bakiyeCol) = GuessColumns(table);
            var lastSnapshotDate = await _snapshots.GetLastSnapshotDateAsync(KasaRaporTuru.Genel, ct);

            var defaults = await _globalDefaults.GetAsync(ct);
            var defaultVergiList = new List<string>();
            try
            {
                defaultVergiList = JsonSerializer.Deserialize<List<string>>(defaults.SelectedVeznedarlarJson ?? "[]") ?? new();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[KasaPreviewController] VergiKasa JSON parse hatası: {ex.Message}"); defaultVergiList = new(); }

            return new KasaUstRaporPanelViewModel
            {
                Table = table, KasaUstRaporFileName = kasaUst,
                DateEval = eval, ProposedDate = proposed, FinalDate = proposed,
                VeznedarColumn = veznedarCol, BakiyeColumn = bakiyeCol,
                DefaultVergiKasaVeznedarlar = defaultVergiList,
                HasExistingSnapshot = lastSnapshotDate.HasValue, LastSnapshotDate = lastSnapshotDate,
                StartOpen = false, ShowSaveButton = false, Context = "kasapreview"
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "KasaÜstRapor panel hydration failed in KasaPreview");
            return null;
        }
    }

    private List<string> ListUploadedFiles()
    {
        var folder = ResolveUploadFolderAbsolute();
        if (!Directory.Exists(folder)) return new();
        return Directory
            .GetFiles(folder, "*.xls*")
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderByDescending(name => name)
            .ToList();
    }

    private static string? PickKasaUstRaporFile(List<string> files)
    {
        return files.FirstOrDefault(f =>
            f.Contains("kasa", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("ust", StringComparison.OrdinalIgnoreCase));
    }

    private static (string? Veznedar, string? Bakiye) GuessColumns(ImportedTable? table)
    {
        if (table == null) return (null, null);
        var metas = table.ColumnMetas;
        if (metas == null || metas.Count == 0) return (null, null);

        string? veznedar = null, bakiye = null;

        foreach (var c in new[] { "veznedar", "vezne", "kasiyer", "personel", "ad" })
        {
            var hit = metas.FirstOrDefault(m => string.Equals(m.CanonicalName, c, StringComparison.OrdinalIgnoreCase));
            if (hit != null) { veznedar = hit.CanonicalName; break; }
        }
        foreach (var c in new[] { "bakiye", "kasa", "kasada_kalan", "toplam", "tutar", "nakit" })
        {
            var hit = metas.FirstOrDefault(m => string.Equals(m.CanonicalName, c, StringComparison.OrdinalIgnoreCase));
            if (hit != null) { bakiye = hit.CanonicalName; break; }
        }

        veznedar ??= metas.FirstOrDefault()?.CanonicalName;
        bakiye ??= metas.LastOrDefault()?.CanonicalName;

        return (veznedar, bakiye);
    }

    // =========================================================================
    // Validation — Uyarı Sistemi
    // =========================================================================

    /// <summary>
    /// Hesaplama sonrası validation kurallarını çalıştırır ve ViewModel'e yazar.
    /// HasResults=true olduğunda çağrılır.
    /// </summary>
    private async Task HydrateValidationAsync(KasaPreviewViewModel model, CancellationToken ct)
    {
        if (!model.HasResults) return;

        try
        {
            var data = await BuildKasaRaporDataAsync(model, includeUstRapor: false, ct);

            // FormulaRun → Validation override
            {
                decimal OutVal(string key) =>
                    model.FormulaRun?.Outputs != null
                    && model.FormulaRun.Outputs.TryGetValue(key, out var v) ? v : 0m;

                decimal PoolDecimal(string key)
                {
                    var p = model.PoolEntries?.FirstOrDefault(x =>
                        x.CanonicalKey.Equals(key, StringComparison.OrdinalIgnoreCase));
                    if (p != null && decimal.TryParse(p.Value,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var pv))
                        return pv;
                    return 0m;
                }

                data.StopajKontrolFark = OutVal("stopaj_kontrol");
                data.StopajKontrolOk = Math.Abs(data.StopajKontrolFark) < 0.01m;

                // Banka Reconciliation
                data.BankaGirenTahsilat = PoolDecimal("bankaya_giren_tahsilat");
                data.BankaGirenHarc = PoolDecimal("bankaya_giren_harc");
                data.OnlineTahsilat = PoolDecimal("online_tahsilat");
                data.OnlineHarc = PoolDecimal("online_harc");
                data.BankayaTahsilat = OutVal("bankaya_yatirilacak_tahsilat");
                data.BankayaHarc = OutVal("bankaya_yatirilacak_harc");

                data.EftOtomatikIade = PoolDecimal("eft_otomatik_iade");
                data.GelenHavale = PoolDecimal("gelen_havale");
                data.IadeKelimesiGiris = PoolDecimal("iade_kelimesi_giris");
                data.DundenEksikFazlaGelenTahsilat = PoolDecimal("dunden_eksik_fazla_gelen_tahsilat");
                data.DundenEksikFazlaGelenHarc = PoolDecimal("dunden_eksik_fazla_gelen_harc");

                // Bankaya Yatırılacak Doğrulama
                data.BankaMevduatTahsilat = PoolDecimal("banka_mevduat_tahsilat");
                data.BankaVirmanTahsilat = PoolDecimal("banka_virman_tahsilat");
                data.BankaMevduatHarc = PoolDecimal("banka_mevduat_harc");
            }
            // Akıllı Takip Korelasyonu: Validation kurallarına takip bilgisini aktar
            data.TakipCozumBildirim = model.TakipCozumBildirim;
            model.ValidationResults = _validation.Validate(data);

            var raporTarihi = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today);
            var kasaTuru = !string.IsNullOrEmpty(model.KasaType) ? model.KasaType : "Aksam";
            model.DismissedRuleCodes = await _validation.GetDismissedCodesAsync(raporTarihi, kasaTuru, ct);

            if (model.ValidationResults.Count > 0)
            {
                var activeCount = model.ValidationResults.Count(r => !model.DismissedRuleCodes.Contains(r.Code));
                _log.LogInformation("Validation: {Active} aktif, {Dismissed} çözüldü, {Total} toplam uyarı",
                    activeCount, model.ValidationResults.Count - activeCount, model.ValidationResults.Count);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Validation uyarı sistemi çalışırken hata (sonuçlar etkilenmedi)");
        }
    }

    /// <summary>
    /// AJAX POST: Uyarıyı "Çözüldü/Tamamlandı" olarak işaretler.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DismissValidation(
        [FromForm] string raporTarihi,
        [FromForm] string kasaTuru,
        [FromForm] string ruleCode,
        [FromForm] string? note,
        CancellationToken ct)
    {
        try
        {
            if (!DateOnly.TryParse(raporTarihi, out var tarih))
                return Json(new { ok = false, message = "Geçersiz tarih." });

            if (string.IsNullOrWhiteSpace(ruleCode))
                return Json(new { ok = false, message = "Kural kodu boş." });

            await _validation.DismissAsync(tarih, kasaTuru, ruleCode.Trim(), note?.Trim(), null, ct);

            return Json(new { ok = true, message = $"✅ Uyarı çözüldü olarak işaretlendi: {ruleCode}" });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Validation dismiss hatası - {RuleCode}", ruleCode);
            return Json(new { ok = false, message = $"❌ Hata: {ex.Message}" });
        }
    }
}
