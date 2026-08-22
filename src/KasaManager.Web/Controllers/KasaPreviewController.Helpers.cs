using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Validation;
using KasaManager.Web.Helpers;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace KasaManager.Web.Controllers;

// ─────────────────────────────────────────────────────────────
// KasaPreviewController — Helpers (BuildData / Hydration / Validation)
// ─────────────────────────────────────────────────────────────
public sealed partial class KasaPreviewController
{
    // =========================================================================
    // Auto-Provision: Genel Snapshot yoksa dosyalardan otomatik oluştur
    // =========================================================================

    /// <summary>
    /// Sabah/Aksam kasa sayfası açıldığında, seçili tarih için Genel snapshot yoksa
    /// ama KasaÜstRapor Excel dosyası yüklüyse otomatik olarak snapshot oluşturur.
    /// P4.1 stateless geçişinde snapshot kaydı devre dışı bırakılmıştı — bu metot
    /// o boşluğu kapatır ve kullanıcının manuel "Snapshot Kaydet" yapmasını gereksiz kılar.
    /// </summary>
    private async Task TryAutoProvisionGenelSnapshotAsync(DateOnly targetDate, CancellationToken ct)
    {
        try
        {
            // 1) Zaten snapshot var mı?
            var existing = await _raporSnapshots.GetAsync(targetDate, KasaRaporTuru.Genel, ct);
            if (existing?.Rows != null && existing.Rows.Count > 0)
                return; // Var, bir şey yapma

            // 2) KasaÜstRapor dosyası yüklü mü?
            var files = ListUploadedFiles();
            var kasaUstFile = PickKasaUstRaporFile(files);
            if (string.IsNullOrWhiteSpace(kasaUstFile))
                return; // Dosya yok

            var folder = ResolveUploadFolderAbsolute();
            var fullPath = Path.Combine(folder, kasaUstFile);
            if (!System.IO.File.Exists(fullPath))
                return;

            // 3) Dosya tarihini kontrol et — yüklü dosyanın tarih kuralları eşleşiyor mu?
            var eval = await _dateRules.EvaluateAsync(folder, ct);
            var fileDate = eval.ProposedDate ?? targetDate;

            // Dosya tarihi ile hedef tarih uyuşmuyorsa, yine de aynı gün dosyası olduğunu varsay
            // (kullanıcı birden fazla gün çalışabilir, dosya ismi/içeriği dünün olabilir)

            // 4) KasaÜstRapor'u import et
            var import = _importOrchestrator.Import(fullPath, ImportFileKind.KasaUstRapor);
            if (!import.Ok || import.Value is null)
            {
                _log.LogWarning("Auto-provision: KasaÜstRapor import başarısız: {Error}", import.Error);
                return;
            }

            var table = import.Value;
            var (veznedarCol, bakiyeCol) = GuessColumns(table);

            // 5) Tüm satırları seçili olarak snapshot oluştur
            var rows = new List<KasaManager.Domain.Reports.Snapshots.KasaRaporSnapshotRow>();
            decimal selectionTotal = 0m;

            // VergiKasa veznedarlarını Global Defaults'tan al (seçim işaretleme için)
            var defaults = await _globalDefaults.GetAsync(ct);
            var defaultVergiList = new List<string>();
            try
            {
                defaultVergiList = System.Text.Json.JsonSerializer.Deserialize<List<string>>(
                    defaults.SelectedVeznedarlarJson ?? "[]") ?? new();
            }
            catch { defaultVergiList = new(); }

            var headersJson = System.Text.Json.JsonSerializer.Serialize(table.ColumnMetas);

            for (int i = 0; i < table.Rows.Count; i++)
            {
                var r = table.Rows[i];
                var veznedar = (veznedarCol != null && r.TryGetValue(veznedarCol, out var vz)) ? vz ?? "" : "";
                var isSummary = IsSummaryRow(r);

                // VergiKasa veznedarlarını "seçili" olarak işaretle
                var isSelected = !isSummary
                    && !string.IsNullOrWhiteSpace(veznedar)
                    && defaultVergiList.Contains(veznedar.Trim(), StringComparer.OrdinalIgnoreCase);

                decimal bakiye = 0m;
                if (bakiyeCol != null && r.TryGetValue(bakiyeCol, out var bv))
                    Application.Services.Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(bv, out bakiye);

                if (isSelected)
                    selectionTotal += bakiye;

                rows.Add(new KasaManager.Domain.Reports.Snapshots.KasaRaporSnapshotRow
                {
                    Veznedar = veznedar,
                    IsSelected = isSelected,
                    Bakiye = bakiye,
                    IsSummaryRow = isSummary,
                    ColumnsJson = System.Text.Json.JsonSerializer.Serialize(r),
                    HeadersJson = i == 0 ? headersJson : null
                });
            }

            var snapshot = new KasaManager.Domain.Reports.Snapshots.KasaRaporSnapshot
            {
                RaporTarihi = targetDate,
                RaporTuru = KasaRaporTuru.Genel,
                Version = 1,
                SelectionTotal = selectionTotal,
                CreatedBy = "AutoProvision",
                SourceEvidenceJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Source = "AutoProvision",
                    File = kasaUstFile,
                    FileDate = fileDate.ToString("yyyy-MM-dd"),
                    TargetDate = targetDate.ToString("yyyy-MM-dd"),
                    RowCount = rows.Count
                }),
                Rows = rows
            };

            await _raporSnapshots.SaveAsync(snapshot, ct);

            _log.LogInformation(
                "Auto-provision: {TargetDate} için Genel snapshot otomatik oluşturuldu ({RowCount} satır, dosya={File})",
                targetDate, rows.Count, kasaUstFile);
        }
        catch (Exception ex)
        {
            // Auto-provision sessiz başarısız — kullanıcıyı engellemesin
            _log.LogWarning(ex, "Auto-provision snapshot oluşturma başarısız (tarih={Date})", targetDate);
        }
    }

    private static bool IsSummaryRow(Dictionary<string, string?> row)
    {
        foreach (var kv in row)
        {
            var v = kv.Value;
            if (string.IsNullOrWhiteSpace(v)) continue;
            var t = v.Trim().ToLowerInvariant();
            if (t == "toplam" || t.Contains("genel toplam") || t.Contains("toplamlar"))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Hidden input'lardan posted değerleri okuyarak KasaRaporData oluşturur.
    /// Engine tekrar çalıştırılmaz — ekrandaki değerler birebir kullanılır.
    /// </summary>
    private async Task<KasaManager.Domain.Reports.KasaRaporData> BuildKasaRaporDataAsync(
        KasaPreviewViewModel model, bool includeUstRapor, CancellationToken ct)
    {
        decimal PF(string name)
            => TryParseAmount(Request.Form[name].ToString(), $"Request.Form[{name}]", out var value)
                ? value
                : 0m;

        // FIX: Quick Save formu Rpt... hidden alanlarını içermez.
        // Form alanı gerçekten gönderildiyse form değerini, gönderilmediyse model
        // property'sini (MVC binding ile doğru gelen) kullanır.
        // Bilinçli 0 girişleri korunur çünkü ContainsKey ile varlık kontrolü yapılır.
        decimal PFM(string formKey, decimal? modelValue)
            => Request.Form.ContainsKey(formKey) ? PF(formKey) : (modelValue ?? 0m);

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
            VergidenGelen = PF("RptVergidenGelen"), VergiKasa = PF("VergiKasaBakiyeToplam"), VergideBirikenKasa = PF("RptVergideBiriken"),
            // Beklenen Girişler
            EftOtomatikIade = PF("RptEftIade"), GelenHavale = PF("RptGelenHavale"), IadeKelimesiGiris = PF("RptIadeKelimesi"),
            // Banka Reconciliation
            BankaGirenTahsilat = PF("RptBankaGirenTahsilat"), BankaGirenHarc = PF("RptBankaGirenHarc"),
            OnlineTahsilat = PF("RptOnlineTahsilat"), OnlineHarc = PF("RptOnlineHarc"),
            // Eksik/Fazla — PFM: Quick Save'de Rpt alanları yoksa model property fallback
            IsSabahKasa = isSabah,
            GuneAitEksikFazlaTahsilat = PFM("RptEfGuneT", model.GuneAitEksikFazlaTahsilat),
            DundenEksikFazlaTahsilat = PFM("RptEfDundenT", model.DundenEksikFazlaTahsilat),
            DundenEksikFazlaGelenTahsilat = PFM("RptEfGelenT", model.DundenEksikFazlaGelenTahsilat),
            GuneAitEksikFazlaHarc = PFM("RptEfGuneH", model.GuneAitEksikFazlaHarc),
            DundenEksikFazlaHarc = PFM("RptEfDundenH", model.DundenEksikFazlaHarc),
            DundenEksikFazlaGelenHarc = PFM("RptEfGelenH", model.DundenEksikFazlaGelenHarc),
            // ROW 4D: Bankaya Yatırılacak Doğrulama (Sabah Kasa)
            BankaMevduatTahsilat = PF("RptBankaMevduatTahsilat"),
            BankaVirmanTahsilat = PF("RptBankaVirmanTahsilat"), BankaMevduatHarc = PF("RptBankaMevduatHarc"),
            // Kullanıcı Girişleri (Ek Girdiler — Opsiyonel)
            KaydenTahsilat = PF("RptKaydenTahsilat"),
            KaydenHarc = PF("RptKaydenHarc"),
            CesitliNedenlerleBankadanCikamayanTahsilat = PF("RptCNBCikamayan"),
            BankayaYatirilacakTahsilatiDegistir = PF("RptBankayaYatirilacakTahsilatiDegistir"),
            BankayaYatirilacakHarciDegistir = PF("RptBankayaYatirilacakHarciDegistir"),
            BankayaGonderilmisDeger = PF("RptBankayaGonderilmisDeger"),
            KasadaKalacakHedef = PF("RptKasadaKalacakHedef"),
            // Manuel Girdiler
            BozukPara = PF("RptBozukPara"),
            NakitPara = PF("RptNakitPara"),
            GelmeyenD = PF("RptGelmeyenD"),
            // Validation İletileri
            ValidationMessagesJson = Request.Form["RptValidationMessagesJson"].ToString(),
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

    /// <summary>
    /// Builds the version-2 immutable audit payload exclusively from the
    /// server-side Hesap Kontrol query for the canonical save date.
    /// Exceptions intentionally propagate so SaveReport cannot persist a
    /// partial version-2 payload.
    /// </summary>
    private async Task<(KasaImmutableAuditData Summary, HesapKontrolImmutableAuditDetails Details)>
        BuildImmutableAuditAsync(
        DateOnly raporTarihi,
        CancellationToken ct)
    {
        var snapshot = await _hesapKontrol.GetImmutableAuditSnapshotAsync(raporTarihi, ct);
        if (!HesapKontrolImmutableAuditDetailsValidator.TryValidate(
                snapshot.Details,
                out var validationError))
        {
            throw new InvalidOperationException(
                $"Immutable Hesap Kontrol audit details validation failed: {validationError}");
        }

        var fill = snapshot.Summary;

        var summary = new KasaImmutableAuditData
        {
            GuneAitEksikFazlaTahsilat = fill.GuneAitEksikFazlaTahsilat,
            GuneAitEksikFazlaHarc = fill.GuneAitEksikFazlaHarc,
            OncekiGunAcikTahsilat = fill.OncekiGunAcikTahsilat,
            OncekiGunAcikHarc = fill.OncekiGunAcikHarc,
            BugunCozulenTahsilat = fill.BugunCozulenTahsilat,
            BugunCozulenHarc = fill.BugunCozulenHarc,
            TakipteEksikTahsilat = fill.TakipteEksikTahsilat,
            TakipteEksikHarc = fill.TakipteEksikHarc,
            TakipteFazlaTahsilat = fill.TakipteFazlaTahsilat,
            TakipteFazlaHarc = fill.TakipteFazlaHarc,
            TakipteSayisi = fill.TakipteSayisi,
            ToplamFarkTahsilat = fill.ToplamFarkTahsilat,
            ToplamFarkHarc = fill.ToplamFarkHarc,
            BeklenenTahsilat = fill.BeklenenTahsilat,
            BeklenenHarc = fill.BeklenenHarc,
            OlaganDisiTahsilat = fill.OlaganDisiTahsilat,
            OlaganDisiHarc = fill.OlaganDisiHarc,
            TakipKasaEtkisiTahsilat = fill.TakipKasaEtkisiTahsilat,
            TakipKasaEtkisiHarc = fill.TakipKasaEtkisiHarc,
            TakipKasaEtkisiNet = fill.TakipKasaEtkisiNet,
            BreakdownMesajTahsilat = fill.BreakdownMesajTahsilat,
            BreakdownMesajHarc = fill.BreakdownMesajHarc
        };

        if (snapshot.Details.Records.Count == 0 && !IsZeroImmutableAuditSummary(summary))
        {
            throw new InvalidOperationException(
                "Immutable Hesap Kontrol audit summary/details parity failed: nonzero-empty");
        }

        return (summary, snapshot.Details);
    }

    private static bool TryReadHistoricalImmutableAudit(
        string? persistedPayloadJson,
        out (KasaImmutableAuditData Summary, HesapKontrolImmutableAuditDetails Details) audit,
        out int payloadVersion,
        out string error)
    {
        audit = default;
        payloadVersion = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(persistedPayloadJson))
        {
            error = "payload-empty";
            return false;
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var payload = JsonSerializer.Deserialize<KasaRaporData>(persistedPayloadJson, options);
            if (payload is null || payload.PayloadVersion is not (1 or 2))
            {
                error = "payload-version";
                return false;
            }

            if (!IsValidImmutableAuditSummary(payload.ImmutableAudit))
            {
                error = "summary-invalid";
                return false;
            }

            payloadVersion = payload.PayloadVersion;
            if (payload.PayloadVersion == 1)
            {
                // V1 persisted the server-derived scalar audit but had no record-detail
                // contract. Preserve that contract on compatibility saves; never invent
                // details and never fall back to request-bound financial values.
                audit = (payload.ImmutableAudit!, new HesapKontrolImmutableAuditDetails(
                    Array.Empty<HesapKontrolImmutableAuditRecord>(),
                    new HesapKontrolImmutableAuditGroups(
                        Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>(),
                        Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>())));
                return true;
            }

            if (!payload.ImmutableAuditDetails.HasValue
                || payload.ImmutableAuditDetails.Value.ValueKind
                    is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                error = "details-missing";
                return false;
            }

            var details = payload.ImmutableAuditDetails.Value
                .Deserialize<HesapKontrolImmutableAuditDetails>(options);
            if (details?.Records is null
                || details.Groups is null
                || details.Groups.AktifKayitlar is null
                || details.Groups.OncekiAciklar is null
                || details.Groups.BugunCozulenler is null
                || details.Groups.ReconciliationKayitlar is null
                || details.Groups.TakipteKayitlar is null
                || details.Groups.BugunTakipCozulenler is null)
            {
                error = "details-null-member";
                return false;
            }

            // Re-materialize only the safe 13-field DTO and canonical ordering.
            // Extra JSON members (actor, notes, path/source metadata) cannot flow
            // into the new immutable audit payload through this boundary.
            var normalizedDetails = new HesapKontrolImmutableAuditDetails(
                HesapKontrolImmutableAuditDetailsValidator.OrderRecords(details.Records),
                new HesapKontrolImmutableAuditGroups(
                    details.Groups.AktifKayitlar.OrderBy(id => id).ToArray(),
                    details.Groups.OncekiAciklar.OrderBy(id => id).ToArray(),
                    details.Groups.BugunCozulenler.OrderBy(id => id).ToArray(),
                    details.Groups.ReconciliationKayitlar.OrderBy(id => id).ToArray(),
                    details.Groups.TakipteKayitlar.OrderBy(id => id).ToArray(),
                    details.Groups.BugunTakipCozulenler.OrderBy(id => id).ToArray()));

            if (!HesapKontrolImmutableAuditDetailsValidator.TryValidate(
                    normalizedDetails,
                    out var validationError))
            {
                error = validationError ?? "details-invalid";
                return false;
            }

            if (normalizedDetails.Records.Count == 0
                && !IsZeroImmutableAuditSummary(payload.ImmutableAudit!))
            {
                error = "summary-details-nonzero-empty";
                return false;
            }

            audit = (payload.ImmutableAudit!, normalizedDetails);
            return true;
        }
        catch (JsonException)
        {
            error = "payload-json";
            return false;
        }
        catch (NotSupportedException)
        {
            error = "payload-not-supported";
            return false;
        }
    }

    private static bool IsValidImmutableAuditSummary(KasaImmutableAuditData? audit) =>
        audit is not null && audit.TakipteSayisi >= 0;

    private static bool IsZeroImmutableAuditSummary(KasaImmutableAuditData audit) =>
        audit.GuneAitEksikFazlaTahsilat == 0m
        && audit.GuneAitEksikFazlaHarc == 0m
        && audit.OncekiGunAcikTahsilat == 0m
        && audit.OncekiGunAcikHarc == 0m
        && audit.BugunCozulenTahsilat == 0m
        && audit.BugunCozulenHarc == 0m
        && audit.TakipteEksikTahsilat == 0m
        && audit.TakipteEksikHarc == 0m
        && audit.TakipteFazlaTahsilat == 0m
        && audit.TakipteFazlaHarc == 0m
        && audit.TakipteSayisi == 0
        && audit.ToplamFarkTahsilat == 0m
        && audit.ToplamFarkHarc == 0m
        && audit.BeklenenTahsilat == 0m
        && audit.BeklenenHarc == 0m
        && audit.OlaganDisiTahsilat == 0m
        && audit.OlaganDisiHarc == 0m
        && audit.TakipKasaEtkisiTahsilat == 0m
        && audit.TakipKasaEtkisiHarc == 0m
        && audit.TakipKasaEtkisiNet == 0m;

    private static void ApplyImmutableAuditSummary(
        KasaPreviewViewModel model,
        KasaImmutableAuditData audit)
    {
        model.GuneAitEksikFazlaTahsilat = audit.GuneAitEksikFazlaTahsilat;
        model.GuneAitEksikFazlaHarc = audit.GuneAitEksikFazlaHarc;
        model.DundenEksikFazlaTahsilat = audit.OncekiGunAcikTahsilat;
        model.DundenEksikFazlaHarc = audit.OncekiGunAcikHarc;
        model.DundenEksikFazlaGelenTahsilat = audit.BugunCozulenTahsilat;
        model.DundenEksikFazlaGelenHarc = audit.BugunCozulenHarc;
        model.TakipteEksikTahsilat = audit.TakipteEksikTahsilat;
        model.TakipteEksikHarc = audit.TakipteEksikHarc;
        model.TakipteFazlaTahsilat = audit.TakipteFazlaTahsilat;
        model.TakipteFazlaHarc = audit.TakipteFazlaHarc;
        model.TakipteSayisi = audit.TakipteSayisi;
        model.ToplamFarkTahsilat = audit.ToplamFarkTahsilat;
        model.ToplamFarkHarc = audit.ToplamFarkHarc;
        model.BeklenenTahsilat = audit.BeklenenTahsilat;
        model.BeklenenHarc = audit.BeklenenHarc;
        model.OlaganDisiTahsilat = audit.OlaganDisiTahsilat;
        model.OlaganDisiHarc = audit.OlaganDisiHarc;
        model.TakipKasaEtkisiTahsilat = audit.TakipKasaEtkisiTahsilat;
        model.TakipKasaEtkisiHarc = audit.TakipKasaEtkisiHarc;
        model.TakipKasaEtkisiNet = audit.TakipKasaEtkisiNet;
        model.BreakdownMesajTahsilat = audit.BreakdownMesajTahsilat;
        model.BreakdownMesajHarc = audit.BreakdownMesajHarc;
    }

    private static bool TryApplyImmutableAuditDetails(
        KasaPreviewViewModel model,
        JsonElement? serializedDetails,
        KasaImmutableAuditData scalarAudit)
    {
        if (!serializedDetails.HasValue
            || serializedDetails.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return false;

        try
        {
            var details = serializedDetails.Value.Deserialize<HesapKontrolImmutableAuditDetails>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (!HesapKontrolImmutableAuditDetailsValidator.TryValidate(details, out _))
                return false;
            if (details!.Records.Count == 0 && !IsZeroImmutableAuditSummary(scalarAudit))
                return false;

            model.ImmutableAuditRecords = details.Records
                .Select(record => new ImmutableAuditRecordViewModel(
                    record.KayitId,
                    record.AnalizTarihi,
                    record.HesapTuru,
                    record.Yon,
                    record.Tutar,
                    record.KaydetmeAnindakiDurum,
                    record.Sinif,
                    record.DosyaNo,
                    record.BirimAdi,
                    record.TespitEdilenTip,
                    record.TakipBaslangicTarihi,
                    record.CozulmeTarihi,
                    record.OnayTarihi))
                .ToArray();
            model.ImmutableAuditRecordGroups = new ImmutableAuditRecordGroupsViewModel(
                details.Groups.AktifKayitlar.ToArray(),
                details.Groups.OncekiAciklar.ToArray(),
                details.Groups.BugunCozulenler.ToArray(),
                details.Groups.ReconciliationKayitlar.ToArray(),
                details.Groups.TakipteKayitlar.ToArray(),
                details.Groups.BugunTakipCozulenler.ToArray());
            model.HasImmutableAuditRecordDetails = true;
            model.ImmutableAuditRecordDetailsNotice = null;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    // =========================================================================
    // Private Helpers
    // =========================================================================

    /// <summary>
    /// B6: HesapKontrol'den Sabah Kasa eksik/fazla alanlarını otomatik doldurur.
    /// </summary>
    private async Task TryAutoFillEksikFazlaAsync(KasaPreviewViewModel model, CancellationToken ct)
    {
        model.HasImmutableAuditData = false;
        model.ImmutableAuditNotice = null;
        model.LoadedAuditPayloadVersion = 0;
        model.HasImmutableAuditRecordDetails = false;
        model.ImmutableAuditRecordDetailsNotice = null;
        model.ImmutableAuditRecords = Array.Empty<ImmutableAuditRecordViewModel>();
        model.ImmutableAuditRecordGroups = ImmutableAuditRecordGroupsViewModel.Empty;

        try
        {
            var beforeGuneT = model.GuneAitEksikFazlaTahsilat;
            var beforeGuneH = model.GuneAitEksikFazlaHarc;
            var beforeDundenT = model.DundenEksikFazlaTahsilat;
            var beforeDundenH = model.DundenEksikFazlaHarc;

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
            model.TakipKasaEtkisiTahsilat = fill.TakipKasaEtkisiTahsilat;
            model.TakipKasaEtkisiHarc = fill.TakipKasaEtkisiHarc;
            model.TakipKasaEtkisiNet = fill.TakipKasaEtkisiNet;
            model.BeklenenTahsilat = fill.BeklenenTahsilat;
            model.BeklenenHarc = fill.BeklenenHarc;
            model.OlaganDisiTahsilat = fill.OlaganDisiTahsilat;
            model.OlaganDisiHarc = fill.OlaganDisiHarc;
            model.BreakdownMesajTahsilat = fill.BreakdownMesajTahsilat;
            model.BreakdownMesajHarc = fill.BreakdownMesajHarc;
            // Akıllı Takip Korelasyonu
            model.TakipCozumleri = fill.TakipCozumleri;
            model.TakipCozumBildirim = fill.TakipCozumBildirim;
            model.HasImmutableAuditData = true;

            // BUG-2 FIX: CrossDay burada tekrar çağrılmıyor.
            // AnalyzeFromComparisonAsync zaten CrossDayReconcileAsync'i çalıştırır.
            // Çift çalıştırma gereksiz DB yükü ve potansiyel orphan sorunu yaratıyordu.
            _log.LogDebug(
                "[HK-AUTOFILL-DIFF] HasData={HasData} GuneAitEksikFazlaTahsilat: {BeforeGuneT} -> {AfterGuneT} Changed={ChangedGuneT} GuneAitEksikFazlaHarc: {BeforeGuneH} -> {AfterGuneH} Changed={ChangedGuneH} DundenEksikFazlaTahsilat: {BeforeDundenT} -> {AfterDundenT} Changed={ChangedDundenT} DundenEksikFazlaHarc: {BeforeDundenH} -> {AfterDundenH} Changed={ChangedDundenH} TakipKasaEtkisiNet={TakipKasaEtkisiNet}",
                fill.HasData,
                beforeGuneT, model.GuneAitEksikFazlaTahsilat, beforeGuneT != model.GuneAitEksikFazlaTahsilat,
                beforeGuneH, model.GuneAitEksikFazlaHarc, beforeGuneH != model.GuneAitEksikFazlaHarc,
                beforeDundenT, model.DundenEksikFazlaTahsilat, beforeDundenT != model.DundenEksikFazlaTahsilat,
                beforeDundenH, model.DundenEksikFazlaHarc, beforeDundenH != model.DundenEksikFazlaHarc,
                model.TakipKasaEtkisiNet);
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
            
            // Sadece display alanını set et — VergiKasaBakiyeToplam formdan gelmeli
            // result.TotalVergiKasa kümülatif ledger toplamıdır, günlük vergiKasa değildir
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

    private async Task TryRunHesapKontrolAnalysisAsync(
        KasaPreviewViewModel model,
        DateOnly analizTarihi,
        string baseFolder,
        string actionName,
        int actorUserId,
        CancellationToken ct)
    {
        var source = _hesapKontrolSourceResolver.Resolve(baseFolder, analizTarihi);
        if (source.IsValid
            && source.SourceKind == HesapKontrolSourceKind.Current
            && analizTarihi != DateOnly.FromDateTime(DateTime.Today))
        {
            source = HesapKontrolSourceResolution.Fail(
                "Geçmiş tarih için yalnız tarihli arşiv kaynağı kullanılabilir.",
                $"Historical source resolved to current folder. Date={analizTarihi:yyyy-MM-dd}.");
        }
        if (!source.IsValid)
        {
            _log.LogWarning(
                "[HK-PREVIEW-GATE] Analiz kaynağı doğrulanamadı. Action={Action} Date={Date} Detail={Detail}",
                actionName, analizTarihi, source.TechnicalError);
            model.Errors.Add(
                "Seçilen tarih için gerekli Excel kaynakları doğrulanamadı. " +
                "Eksik veya geçersiz dosyaları kontrol ederek tekrar deneyin.");
            return;
        }

        try
        {
            var rapor = await _hesapKontrol.AnalyzeFromComparisonAsync(
                analizTarihi, source.Folder!, actorUserId, ct);
            _log.LogInformation(
                "HesapKontrol analiz tamamlandı. Action={Action} Date={Date} Ozet={Ozet}",
                actionName, analizTarihi, rapor.OzetMesaj);
            if (rapor.OzetMesaj?.Contains("Uyarı:", StringComparison.Ordinal) == true)
                model.Warnings.Add(rapor.OzetMesaj);
            await TryAutoFillEksikFazlaAsync(model, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                ex,
                "HesapKontrol otomatik analiz başarısız. Action={Action} Date={Date}; sonuçlar etkilenmedi",
                actionName, analizTarihi);
            model.Errors.Add(
                "Hesap kontrol analizi tamamlanamadı. Lütfen dosyaları kontrol ederek tekrar deneyin.");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunHesapKontrolFromContext(
        string? kasaType,
        CancellationToken ct)
    {
        if (!TryResolveHesapKontrolActor(out var actorUserId))
            return Unauthorized();

        var normalizedType = string.IsNullOrWhiteSpace(kasaType)
            ? string.Empty
            : NormalizeKasaType(kasaType);
        if (normalizedType is not ("Sabah" or "Aksam"))
            return FailKasaContextTransition(normalizedType,
                "Hesap Kontrol için geçerli bir Sabah/Akşam kasa bağlamı seçilmedi.");

        var userName = _currentUser.Username;
        if (string.IsNullOrWhiteSpace(userName))
            return FailKasaContextTransition(normalizedType,
                "Oturum kullanıcısı doğrulanamadığı için Hesap Kontrol analizi başlatılmadı.");

        var snapshot = await KasaDraftCacheHelper.TryLoadDraftSnapshotAsync(
            userName, normalizedType, _log);
        var draft = snapshot?.Model;
        var context = snapshot?.SourceContext;
        if (draft is null
            || !draft.HasResults
            || draft.Errors.Count > 0
            || !draft.SelectedDate.HasValue
            || !string.Equals(NormalizeKasaType(draft.KasaType), normalizedType,
                StringComparison.Ordinal))
        {
            return FailKasaContextTransition(normalizedType,
                "Bu kasa için başarılı ve güncel bir hesaplama taslağı bulunamadı. Önce kasayı yeniden hesaplayın.");
        }

        if (context is null
            || context.Version != 1
            || context.SelectedDate != draft.SelectedDate.Value
            || !string.Equals(context.KasaType, normalizedType, StringComparison.Ordinal)
            || !string.Equals(context.SourceKind, nameof(HesapKontrolSourceKind.Current),
                StringComparison.Ordinal)
            || !string.Equals(context.SourceIdentifier, "current-upload-bundle",
                StringComparison.Ordinal))
        {
            return FailKasaContextTransition(normalizedType,
                "Kasa hesaplamasının Excel kaynak bağlamı doğrulanamadı. Kasayı mevcut dosyalarla yeniden hesaplayın.");
        }

        var uploadPath = ResolveUploadFolderAbsolute();
        var sourceSnapshot = await TryCreateVerifiedKasaSourceSnapshotAsync(
            uploadPath, context, ct);
        if (sourceSnapshot is null)
        {
            _log.LogWarning(
                "[HK-KASA-CONTEXT] Kaynak paketi hesaplama sonrasında değişti. User={User} KasaType={KasaType} Date={Date}",
                userName, normalizedType, draft.SelectedDate);
            return FailKasaContextTransition(normalizedType,
                "Hesaplamadan sonra Excel kaynakları değişti. Güvenlik için analiz başlatılmadı; kasayı yeniden hesaplayın.");
        }

        try
        {
            var report = await _hesapKontrol.AnalyzeFromComparisonAsync(
                draft.SelectedDate.Value, sourceSnapshot, actorUserId, ct);
            TempData["Info"] = $"Hesap Kontrol analizi tamamlandı: {report.OzetMesaj}";
            return RedirectToAction("Index", "HesapKontrol", new
            {
                analizTarihiStr = draft.SelectedDate.Value.ToString("yyyy-MM-dd"),
                tab = "takipte"
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "[HK-KASA-CONTEXT] Analiz tamamlanamadı. User={User} KasaType={KasaType} Date={Date}",
                userName, normalizedType, draft.SelectedDate);
            return FailKasaContextTransition(normalizedType,
                "Hesap Kontrol analizi tamamlanamadı. Excel kaynaklarını kontrol edip tekrar deneyin.");
        }
        finally
        {
            TryDeleteKasaSourceSnapshot(sourceSnapshot);
        }
    }

    private IActionResult FailKasaContextTransition(string kasaType, string message)
    {
        TempData["Error"] = message;
        return RedirectToAction(nameof(Index), new
        {
            kasaType = string.IsNullOrWhiteSpace(kasaType) ? null : kasaType
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunHesapKontrolFromSavedSnapshot(
        Guid savedSnapshotId,
        CancellationToken ct)
    {
        if (!TryResolveHesapKontrolActor(out var actorUserId))
            return Unauthorized();

        var userName = _currentUser.Username;
        if (string.IsNullOrWhiteSpace(userName))
            return FailSavedSnapshotTransition(
                savedSnapshotId,
                "Oturum kullanıcısı doğrulanamadığı için kayıtlı rapor analizi başlatılmadı.",
                canReload: false);

        var savedSnapshot = await _calcSnapshots.GetByIdAsync(savedSnapshotId, ct);
        if (savedSnapshot is null)
            return FailSavedSnapshotTransition(
                savedSnapshotId,
                "Kayıtlı Kasa raporu bulunamadı.",
                canReload: false);

        if (savedSnapshot.IsDeleted || !savedSnapshot.IsActive)
            return FailSavedSnapshotTransition(
                savedSnapshotId,
                "Silinmiş veya pasif bir Kasa raporu analiz edilemez.",
                canReload: false);

        var kasaType = savedSnapshot.KasaTuru switch
        {
            KasaRaporTuru.Sabah => "Sabah",
            KasaRaporTuru.Aksam => "Aksam",
            _ => null
        };
        if (kasaType is null)
            return FailSavedSnapshotTransition(
                savedSnapshotId,
                "Yalnız Sabah veya Akşam Kasa raporları Hesap Kontrol'e aktarılabilir.",
                canReload: true);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var retentionDays = Math.Max(
            1, _cfg.GetValue<int>("Comparison:ArchiveRetentionDays", 60));
        var earliestAllowedDate = today.AddDays(-retentionDays);
        if (savedSnapshot.RaporTarihi < earliestAllowedDate
            || savedSnapshot.RaporTarihi > today)
        {
            return FailSavedSnapshotTransition(
                savedSnapshotId,
                $"Rapor tarihi izin verilen {retentionDays} günlük arşiv penceresinin dışında.",
                canReload: true);
        }

        var baseFolder = ResolveUploadFolderAbsolute();
        var source = _hesapKontrolSourceResolver.Resolve(
            baseFolder, savedSnapshot.RaporTarihi);
        if (!source.IsValid
            || source.SourceKind != HesapKontrolSourceKind.Archive
            || !IsCanonicalHistoricalArchiveFolder(
                baseFolder, source.Folder, savedSnapshot.RaporTarihi))
        {
            _log.LogWarning(
                "[HK-SAVED-SNAPSHOT] Historical archive çözülemedi. Snapshot={SnapshotId} Date={Date} Kind={Kind} Detail={Detail}",
                savedSnapshot.Id, savedSnapshot.RaporTarihi, source.SourceKind,
                source.TechnicalError);
            return FailSavedSnapshotTransition(
                savedSnapshotId,
                "Kayıtlı raporun tarihli arşiv kaynağı doğrulanamadı. Current kaynak kullanılmadı.",
                canReload: true);
        }

        var archiveContext = await CaptureKasaDraftSourceContextAsync(
            source.Folder!, savedSnapshot.RaporTarihi, kasaType, ct);
        if (archiveContext is null)
            return FailSavedSnapshotTransition(
                savedSnapshotId,
                "Tarihsel arşiv paketinin fingerprint'i üretilemedi.",
                canReload: true);

        var sourceSnapshot = await TryCreateVerifiedKasaSourceSnapshotAsync(
            source.Folder!, archiveContext, ct);
        if (sourceSnapshot is null)
            return FailSavedSnapshotTransition(
                savedSnapshotId,
                "Tarihsel arşiv paketi doğrulama sırasında değişti; analiz başlatılmadı.",
                canReload: true);

        try
        {
            var report = await _hesapKontrol.AnalyzeFromComparisonAsync(
                savedSnapshot.RaporTarihi, sourceSnapshot, actorUserId, ct);
            TempData["Info"] = $"Hesap Kontrol analizi tamamlandı: {report.OzetMesaj}";
            return RedirectToAction("QueryDate", "HesapKontrol", new
            {
                tarih = savedSnapshot.RaporTarihi.ToString("yyyy-MM-dd"),
                tab = "takipte"
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "[HK-SAVED-SNAPSHOT] Analiz tamamlanamadı. Snapshot={SnapshotId} Date={Date}",
                savedSnapshot.Id, savedSnapshot.RaporTarihi);
            return FailSavedSnapshotTransition(
                savedSnapshotId,
                "Kayıtlı raporun Hesap Kontrol analizi tamamlanamadı.",
                canReload: true);
        }
        finally
        {
            TryDeleteKasaSourceSnapshot(sourceSnapshot);
        }
    }

    private IActionResult FailSavedSnapshotTransition(
        Guid savedSnapshotId,
        string message,
        bool canReload)
    {
        TempData["Error"] = message;
        return canReload
            ? RedirectToAction(nameof(LoadSnapshot), new { id = savedSnapshotId })
            : RedirectToAction("Index", "KasaRaporlar");
    }

    private bool TryResolveHesapKontrolActor(out int actorUserId)
    {
        try
        {
            actorUserId = _currentUser.RequireAuthenticatedUserId();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            actorUserId = default;
            return false;
        }
    }

    private static bool IsCanonicalHistoricalArchiveFolder(
        string baseFolder,
        string? resolvedFolder,
        DateOnly reportDate)
    {
        if (string.IsNullOrWhiteSpace(resolvedFolder))
            return false;

        try
        {
            var expected = Path.GetFullPath(Path.Combine(
                baseFolder, "archive", reportDate.ToString("yyyy-MM-dd")));
            var actual = Path.GetFullPath(resolvedFolder);
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildLoadDataResultWarning(string? kasaType)
    {
        var guidance = kasaType?.Equals("Aksam", StringComparison.OrdinalIgnoreCase) == true
            ? "Lütfen Akşam Kasa Modu (Mesai Sonu / Tam Gün) seçimini ve yüklenen dosya setini kontrol edin. "
            : "Lütfen bu kasa türü için yüklenen dosya setini kontrol edin. ";

        return "Veriler yüklendi ancak bu mod için hesaplama sonucu üretilemedi. " +
               guidance +
               "Eski sonuçlar güvenlik nedeniyle gösterilmedi.";
    }

    private string ResolveUploadFolderAbsolute()
    {
        var sub = _cfg.GetValue<string>("Upload:SubFolder") ?? "Data\\Raporlar";
        sub = sub.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_env.WebRootPath, sub);
    }

    /// <summary>
    /// Revision 3 PERSISTED SOURCE FRESHNESS CLOSURE: bu metot artık KasaSourceFingerprintHelper'a
    /// delege eden ince (thin) bir wrapper'dır — algoritma DEĞİŞMEDİ (aynı SHA256 manifest formatı),
    /// yalnızca KasaUstRaporController.Save'in de aynı mantığı kullanabilmesi için paylaşılan sınıfa
    /// taşındı. Mevcut çağıranlar (LoadAndCalculate, TryCreateVerifiedKasaSourceSnapshotAsync)
    /// değişmeden çalışmaya devam eder.
    /// </summary>
    private Task<KasaDraftSourceContext?> CaptureKasaDraftSourceContextAsync(
        string uploadFolder,
        DateOnly? selectedDate,
        string kasaType,
        CancellationToken ct)
        => KasaSourceFingerprintHelper.CaptureAsync(uploadFolder, selectedDate, kasaType, _log, ct);

    private async Task<string?> TryCreateVerifiedKasaSourceSnapshotAsync(
        string sourceFolder,
        KasaDraftSourceContext expectedContext,
        CancellationToken ct)
    {
        var snapshotFolder = Path.Combine(
            Path.GetTempPath(), "KasaManager", "HesapKontrolContext", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(snapshotFolder);
            foreach (var sourceFile in Directory.GetFiles(
                         sourceFolder, "*.xls*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                var targetFile = Path.Combine(snapshotFolder, Path.GetFileName(sourceFile));
                await using var input = new FileStream(
                    sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 81920, useAsync: true);
                await using var output = new FileStream(
                    targetFile, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 81920, useAsync: true);
                await input.CopyToAsync(output, ct);
            }

            var snapshotContext = await CaptureKasaDraftSourceContextAsync(
                snapshotFolder,
                expectedContext.SelectedDate,
                expectedContext.KasaType,
                ct);
            if (snapshotContext is not null
                && string.Equals(expectedContext.Fingerprint, snapshotContext.Fingerprint,
                    StringComparison.Ordinal))
            {
                return snapshotFolder;
            }
        }
        catch (OperationCanceledException)
        {
            TryDeleteKasaSourceSnapshot(snapshotFolder);
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Kasa kaynak snapshot'i olusturulamadi.");
        }

        TryDeleteKasaSourceSnapshot(snapshotFolder);
        return null;
    }

    private void TryDeleteKasaSourceSnapshot(string snapshotFolder)
    {
        try
        {
            if (Directory.Exists(snapshotFolder))
                Directory.Delete(snapshotFolder, recursive: true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Kasa kaynak snapshot'i temizlenemedi.");
        }
    }

    /// <summary>Thin wrapper — bkz. CaptureKasaDraftSourceContextAsync'in üstündeki not.</summary>
    private Task<KasaDraftSourceContext?> VerifyKasaDraftSourceContextAsync(
        KasaDraftSourceContext? beforeCalculation,
        string uploadFolder,
        DateOnly? selectedDate,
        string kasaType,
        CancellationToken ct)
        => KasaSourceFingerprintHelper.VerifyAsync(beforeCalculation, uploadFolder, selectedDate, kasaType, _log, ct);

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
                StartOpen = false, ShowSaveButton = false, Context = "kasapreview"
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "KasaÜstRapor panel hydration failed in KasaPreview");
            return null;
        }
    }

    /// <summary>
    /// Eğer UI tarafında kullanıcı açıkça bir Veznedar Checkbox listesi göndermediyse, 
    /// Sistem otomatik olarak Global Ayarlar (Settings) içinden Default Vergi Veznedarlarını okur
    /// ve Excel (KasaUstRapor) içerisinden bu isimleri bularak Bakiye toplamını otomatik hesaplar.
    /// Eski sistemdeki (Snapshot öncesi) otomatik hesaplama mantığının devamıdır.
    /// </summary>
    private async Task ApplyAutoVergiKasaFromDefaultsAsync(KasaPreviewViewModel model, CancellationToken ct)
    {
        // UI'dan explicit bir seçim geldiyse ezme
        if (model.VergiKasaVeznedarlar != null && model.VergiKasaVeznedarlar.Count > 0)
            return;

        // Panel henüz hydrate edilmediyse et (Excel'i parse et)
        model.UstRaporPanel ??= await HydrateUstRaporPanelAsync(ct);

        var defaultList = model.UstRaporPanel?.DefaultVergiKasaVeznedarlar;
        var table = model.UstRaporPanel?.Table;

        if (defaultList == null || defaultList.Count == 0 || table == null)
            return;

        var vezCol = model.UstRaporPanel!.VeznedarColumn ?? "VEZNEDAR";
        var bakCol = model.UstRaporPanel!.BakiyeColumn ?? "BAKİYE";

        decimal total = 0m;
        var appliedVeznedars = new List<string>();

        foreach (var row in table.Rows)
        {
            if (row.ContainsKey(vezCol) && row.ContainsKey(bakCol))
            {
                var v = row[vezCol]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(v) && defaultList.Contains(v, StringComparer.OrdinalIgnoreCase))
                {
                    if (decimal.TryParse(row[bakCol]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out var bakiye))
                    {
                        total += bakiye;
                        if(!appliedVeznedars.Contains(v, StringComparer.OrdinalIgnoreCase))
                            appliedVeznedars.Add(v);
                    }
                }
            }
        }

        if (appliedVeznedars.Count > 0)
        {
            model.VergiKasaVeznedarlar = appliedVeznedars;
            model.VergiKasaBakiyeToplam = total;
            _log.LogInformation("AutoVergiKasaFromSettings: {total} (Settings: {vez})", total, string.Join(",", appliedVeznedars));
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
