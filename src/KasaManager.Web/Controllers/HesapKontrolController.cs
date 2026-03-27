using KasaManager.Application.Abstractions;
using KasaManager.Domain.FinancialExceptions;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace KasaManager.Web.Controllers;

/// <summary>
/// Banka Hesap Kontrol modülü — dashboard, açık kayıt yönetimi, geçmiş.
/// </summary>
[Authorize]
public sealed class HesapKontrolController : Controller
{
    // ─── BUG-1 FIX: Akıllı File-Change Detection Cache ───
    // Aynı dosyalar değişmeden sayfa yeniden yüklendiğinde analiz tekrar çalışmaz.
    private static string? _lastAnalysisFileHash;
    private static DateOnly? _lastAnalysisTarih;
    private static readonly object _analysisLock = new();
    private readonly IBankaHesapKontrolService _service;
    private readonly IKasaRaporSnapshotService _snapshots;
    private readonly IHesapKontrolExportService _export;
    private readonly IFinansalIstisnaService _finansalIstisna;
    private readonly ILogger<HesapKontrolController> _log;
    private readonly IWebHostEnvironment _env;

    public HesapKontrolController(
        IBankaHesapKontrolService service,
        IKasaRaporSnapshotService snapshots,
        IHesapKontrolExportService export,
        IFinansalIstisnaService finansalIstisna,
        ILogger<HesapKontrolController> log,
        IWebHostEnvironment env)
    {
        _service = service;
        _snapshots = snapshots;
        _export = export;
        _finansalIstisna = finansalIstisna;
        _log = log;
        _env = env;
    }

    // ─── C1: Dashboard + Açık Kayıt Listesi ───

    [HttpGet]
    public async Task<IActionResult> Index(
        string? tab,
        string? analizTarihiStr,
        BankaHesapTuru? hesapTuru,
        KayitDurumu? durum,
        KayitDurumu? takipDurum,
        string? baslangic,
        string? bitis,
        string? arama,
        CancellationToken ct)
    {
        // Snapshot tarihini tek seferde al
        var lastSnapshotDate = await _snapshots.GetLastSnapshotDateAsync(
            KasaManager.Domain.Reports.KasaRaporTuru.Genel, ct)
            ?? DateOnly.FromDateTime(DateTime.Now);

        // ── Kullanıcının seçtiği analiz tarihi (yoksa son snapshot tarihi) ──
        DateOnly analizTarihi;
        if (DateOnly.TryParse(analizTarihiStr, out var parsed))
            analizTarihi = parsed;
        else
            analizTarihi = lastSnapshotDate;

        // ── Mevcut snapshot tarihlerini getir (dropdown için) ──
        List<DateOnly> snapshotTarihleri = new();
        try
        {
            snapshotTarihleri = await _snapshots.GetAllSnapshotDatesAsync(
                KasaManager.Domain.Reports.KasaRaporTuru.Genel, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Snapshot tarihleri alınamadı");
        }

        // ── Otomatik Analiz: Akıllı File-Change Detection ──
        // Dosyalar değişmediyse aynı tarih için analiz tekrar çalışmaz.
        try
        {
            var uploadFolder = Path.Combine(_env.WebRootPath, "Data", "Raporlar");
            var currentHash = ComputeUploadFolderHash(uploadFolder);
            bool shouldAnalyze;

            lock (_analysisLock)
            {
                shouldAnalyze = _lastAnalysisFileHash != currentHash
                             || _lastAnalysisTarih != analizTarihi;
            }

            if (shouldAnalyze)
            {
                var rapor = await _service.AnalyzeFromComparisonAsync(analizTarihi, uploadFolder, ct);
                _log.LogInformation("HesapKontrol otomatik analiz tamamlandı: {Tarih}", analizTarihi);

                lock (_analysisLock)
                {
                    _lastAnalysisFileHash = currentHash;
                    _lastAnalysisTarih = analizTarihi;
                }

                // CrossDay eşleşme sonuçlarını kullanıcıya bildir
                if (rapor.CrossDayEslesmeler.Count > 0)
                {
                    var toplamTutar = rapor.CrossDayEslesmeler.Sum(x => x.Tutar);
                    TempData["CrossDay"] = $"✅ Takipteki {rapor.CrossDayEslesmeler.Count} kayıt DosyaNo doğrulanarak çözüldü! " +
                        $"(Toplam: {toplamTutar:N2} ₺)";
                }
                if (rapor.PotansiyelEslesmeler.Count > 0)
                {
                    var potTutar = rapor.PotansiyelEslesmeler.Sum(x => x.Tutar);
                    TempData["CrossDayPotansiyel"] = $"⚠️ {rapor.PotansiyelEslesmeler.Count} kısmi eşleşme onayınızı bekliyor ({potTutar:N2} ₺)";
                    TempData["PotansiyelEslesmelerJson"] = System.Text.Json.JsonSerializer.Serialize(rapor.PotansiyelEslesmeler);
                }
            }
            else
            {
                _log.LogDebug("HesapKontrol: Dosyalar değişmedi, analiz atlandı (tarih={Tarih})", analizTarihi);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "HesapKontrol otomatik analiz başarısız (sayfa yine de yüklenecek)");
        }

        // ── Servis çağrıları — her biri bağımsız try/catch ile korunur ──
        // Herhangi birinin başarısız olması diğerlerini veya sayfanın render'ını engellemez.

        HesapKontrolDashboard? dashboard = null;
        try
        {
            dashboard = await _service.GetDashboardAsync(analizTarihi, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "HesapKontrol dashboard verisi alınamadı");
        }

        var acikKayitlar = new List<HesapKontrolKaydi>();
        try
        {
            acikKayitlar = await _service.GetOpenItemsAsync(
                hesapTuru: hesapTuru, baslangic: analizTarihi, bitis: analizTarihi, ct: ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "HesapKontrol açık kayıtlar alınamadı");
        }

        var takipteKayitlar = new List<HesapKontrolKaydi>();
        try
        {
            takipteKayitlar = await _service.GetTrackedItemsAsync(
                hesapTuru: hesapTuru, ct: ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "HesapKontrol takipte kayıtlar alınamadı");
        }

        // Geçmiş tab için tarih aralığı
        var startDate = DateOnly.TryParse(baslangic, out var s) ? s : DateOnly.FromDateTime(DateTime.Now.AddDays(-7));
        var endDate = DateOnly.TryParse(bitis, out var e) ? e : DateOnly.FromDateTime(DateTime.Now);

        // Geçmiş verilerini HER ZAMAN yükle — tarih aralığında TÜM durumları getir
        var gecmis = new List<HesapKontrolKaydi>();
        try
        {
            gecmis = await _service.GetHistoryAsync(startDate, endDate, hesapTuru, durum, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "HesapKontrol geçmiş kayıtlar alınamadı");
        }

        // Takip yaşam döngüsü — veritabanından SADECE takibe alınmış kayıtları getir
        var takipGecmisi = new List<HesapKontrolKaydi>();
        if (tab == "takipte")
        {
            try
            {
                takipGecmisi = await _service.GetTrackingLifecycleAsync(
                    startDate, endDate, hesapTuru, takipDurum, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "HesapKontrol takip geçmişi alınamadı");
            }
        }

        // Takip özeti — HER ZAMAN yükle (Dashboard + Takipte sekmesi)
        TakipOzeti? takipOzeti = null;
        try
        {
            takipOzeti = await _service.GetTrackingSummaryAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "HesapKontrol takip özeti alınamadı");
        }

        // D2: Arama filtresi
        if (!string.IsNullOrWhiteSpace(arama))
        {
            bool Matches(HesapKontrolKaydi k) =>
                (k.Aciklama?.Contains(arama, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (k.DosyaNo?.Contains(arama, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (k.BirimAdi?.Contains(arama, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (k.Notlar?.Contains(arama, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (k.TespitEdilenTip?.Contains(arama, StringComparison.OrdinalIgnoreCase) ?? false);

            acikKayitlar = acikKayitlar.Where(Matches).ToList();
            takipteKayitlar = takipteKayitlar.Where(Matches).ToList();
            gecmis = gecmis.Where(Matches).ToList();
            takipGecmisi = takipGecmisi.Where(Matches).ToList();
        }

        var vm = new HesapKontrolViewModel
        {
            Dashboard = dashboard,
            AcikKayitlar = acikKayitlar,
            TakipteKayitlar = takipteKayitlar,
            GecmisKayitlar = gecmis,
            TakipGecmisi = takipGecmisi,
            TakipOzeti = takipOzeti,
            ActiveTab = tab ?? "ozet",
            FilterHesapTuru = hesapTuru,
            FilterDurum = durum,
            FilterBaslangic = startDate,
            FilterBitis = endDate,
            Arama = arama ?? "",
            LastSnapshotDate = lastSnapshotDate,
            AnalizTarihi = analizTarihi,
            SnapshotTarihleri = snapshotTarihleri
        };

        // Potansiyel eşleşmeleri TempData'dan al (CrossDay analizi sırasında kaydedilir)
        if (TempData["PotansiyelEslesmelerJson"] is string potJson)
        {
            try
            {
                vm.PotansiyelEslesmeler = System.Text.Json.JsonSerializer.Deserialize<List<CrossDayMatch>>(potJson) ?? new();
            }
            catch (Exception ex) { _log.LogDebug(ex, "PotansiyelEslesmeler JSON parse hatası"); }
        }

        return View(vm);
    }

    // ─── C2: Onay/İptal İşlemleri ───

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(Guid id, string? not)
    {
        var kullanici = User.Identity?.Name ?? "Anonim";
        var result = await _service.ConfirmMatchAsync(id, kullanici, not);

        if (result)
            TempData["Info"] = "✅ Kayıt başarıyla onaylandı.";
        else
            TempData["Error"] = "❌ Kayıt onaylanamadı. Kayıt bulunamadı veya zaten kapatılmış.";

        return RedirectToAction("Index", new { tab = "acik" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, string? sebep)
    {
        var kullanici = User.Identity?.Name ?? "Anonim";
        var result = await _service.CancelAsync(id, kullanici, sebep);

        if (result)
            TempData["Info"] = "🚫 Kayıt iptal edildi.";
        else
            TempData["Error"] = "❌ Kayıt iptal edilemedi. Kayıt bulunamadı veya zaten kapatılmış.";

        return RedirectToAction("Index", new { tab = "acik" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkCancel(List<Guid> ids)
    {
        var kullanici = User.Identity?.Name ?? "Anonim";
        var count = 0;

        foreach (var id in ids)
        {
            var result = await _service.CancelAsync(id, kullanici, "Toplu yok say");
            if (result) count++;
        }

        TempData["Info"] = $"🚫 {count} kayıt toplu olarak yok sayıldı.";
        return RedirectToAction("Index", new { tab = "acik" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDismissStale(int gunSiniri = 2, CancellationToken ct = default)
    {
        var kullanici = User.Identity?.Name ?? "Anonim";
        var bugun = DateOnly.FromDateTime(DateTime.Today);
        var sinirTarih = bugun.AddDays(-gunSiniri);

        // Eski günlere ait Açık kayıtları bul (Stopaj hariç)
        var eskiAciklar = await _service.GetOpenItemsAsync(ct: ct);
        var staleKayitlar = eskiAciklar
            .Where(k => k.AnalizTarihi < sinirTarih
                     && k.HesapTuru != Domain.Reports.HesapKontrol.BankaHesapTuru.Stopaj)
            .ToList();

        var count = 0;
        foreach (var k in staleKayitlar)
        {
            var result = await _service.CancelAsync(k.Id, kullanici, $"Otomatik temizlik: {(bugun.DayNumber - k.AnalizTarihi.DayNumber)} günlük stale kayıt");
            if (result) count++;
        }

        TempData["Info"] = count > 0
            ? $"🧹 {count} eski açık kayıt temizlendi ({gunSiniri}+ gün öncesi)."
            : "✅ Temizlenecek eski kayıt bulunamadı.";

        return RedirectToAction("Index", new { tab = "acik" });
    }

    // ─── Takip İşlemleri ───

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartTracking(Guid id, string? not)
    {
        var kullanici = User.Identity?.Name ?? "Anonim";
        var result = await _service.StartTrackingAsync(id, kullanici, not);

        if (result)
            TempData["Info"] = "📌 Kayıt takibe alındı. Takipte sekmesinden izleyebilirsiniz.";
        else
            TempData["Error"] = "❌ Kayıt takibe alınamadı. Kayıt bulunamadı veya zaten işlem görmüş.";

        return RedirectToAction("Index", new { tab = "acik" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(Guid id, string? not)
    {
        var kullanici = User.Identity?.Name ?? "Anonim";
        var result = await _service.ResolveTrackedAsync(id, kullanici, not);

        if (result)
            TempData["Info"] = "✅ Takipteki kayıt çözüldü olarak işaretlendi.";
        else
            TempData["Error"] = "❌ Kayıt çözülemedi. Kayıt takipte değil.";

        return RedirectToAction("Index", new { tab = "takipte" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revert(Guid id, string? sebep, string? returnTab)
    {
        var kullanici = User.Identity?.Name ?? "Anonim";
        var result = await _service.RevertAsync(id, kullanici, sebep);

        if (result)
            TempData["Info"] = "↩ Kayıt geri alındı ve Açık Kayıtlar'a döndü.";
        else
            TempData["Error"] = "❌ Kayıt geri alınamadı. Kayıt zaten Açık durumda.";

        return RedirectToAction("Index", new { tab = returnTab ?? "acik" });
    }

    // ─── CrossDay Kısmi Eşleşme Onay/Red ───

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApprovePotentialMatch(Guid eksikId, Guid fazlaId)
    {
        var kullanici = User.Identity?.Name ?? "Anonim";
        var result = await _service.ApprovePotentialMatchAsync(eksikId, fazlaId, kullanici);

        if (result)
            TempData["Info"] = "✅ Kısmi eşleşme onaylandı — kayıtlar çözüldü olarak işaretlendi.";
        else
            TempData["Error"] = "❌ Eşleşme onaylanamadı. Kayıtlar bulunamadı veya işlem görmüş.";

        return RedirectToAction("Index", new { tab = "takipte" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectPotentialMatch(Guid eksikId, Guid fazlaId)
    {
        var kullanici = User.Identity?.Name ?? "Anonim";
        var result = await _service.RejectPotentialMatchAsync(eksikId, fazlaId, kullanici);

        if (result)
            TempData["Info"] = "❌ Kısmi eşleşme reddedildi — kayıt takipte kalmaya devam ediyor.";
        else
            TempData["Error"] = "❌ İşlem başarısız. Kayıt bulunamadı.";

        return RedirectToAction("Index", new { tab = "takipte" });
    }

    // ─── Faz 3: HesapKontrol → Finansal İstisna Oluşturma ───

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFinansalIstisnaFromHK(
        Guid kayitId, int hesapTuru, int yon, decimal tutar,
        string? aciklama, string? analizTarihi, CancellationToken ct)
    {
        try
        {
            var tarih = DateOnly.TryParse(analizTarihi, out var d) ? d : DateOnly.FromDateTime(DateTime.Today);
            var kullanici = User.Identity?.Name ?? "Anonim";

            // HesapKontrol yönüne göre istisna türü ve etki yönü belirle
            var istisnaYon = (KayitYonu)yon;
            var istisnaTur = istisnaYon == KayitYonu.Eksik
                ? IstisnaTuru.BankadanCikamayanTutar
                : IstisnaTuru.SistemeGirilmeyenEft;
            var etkiYonu = istisnaYon == KayitYonu.Eksik
                ? KasaEtkiYonu.Azaltan
                : KasaEtkiYonu.Artiran;

            var request = new FinansalIstisnaCreateRequest(
                IslemTarihi: tarih,
                Tur: istisnaTur,
                Kategori: IstisnaKategorisi.BekleyenSistemGirisi,
                HesapTuru: (BankaHesapTuru)hesapTuru,
                BeklenenTutar: tutar,
                GerceklesenTutar: 0m,
                SistemeGirilenTutar: 0m,
                EtkiYonu: etkiYonu,
                Neden: $"HesapKontrol kaydından oluşturuldu: {aciklama}",
                Aciklama: $"Kaynak HK ID: {kayitId}",
                HedefHesapAciklama: null,
                OlusturanKullanici: kullanici
            );

            await _finansalIstisna.CreateAsync(request, ct);
            TempData["Info"] = $"✅ Finansal istisna oluşturuldu: {tutar:N2} ₺ ({istisnaTur}) — İnceleme bekliyor.";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "HK → Finansal İstisna oluşturma hatası");
            TempData["Error"] = $"❌ İstisna oluşturulamadı: {ex.Message}";
        }

        return RedirectToAction("Index", new { tab = "acik" });
    }

    // ─── D1: Geriye Dönük Analiz ───

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunAnalysis(string? tarih, CancellationToken ct)
    {
        // Formdan gelen tarihi parse et, yoksa en son snapshot tarihini kullan
        DateOnly analizTarihi;
        if (DateOnly.TryParse(tarih, out var d))
        {
            analizTarihi = d;
        }
        else
        {
            var lastSnapshot = await _snapshots.GetLastSnapshotDateAsync(
                KasaManager.Domain.Reports.KasaRaporTuru.Genel, ct);
            analizTarihi = lastSnapshot ?? DateOnly.FromDateTime(DateTime.Now);
        }
        var uploadFolder = Path.Combine(_env.WebRootPath, "Data", "Raporlar");

        try
        {
            var rapor = await _service.AnalyzeFromComparisonAsync(analizTarihi, uploadFolder, ct);
            TempData["Info"] = $"✅ Analiz tamamlandı: {rapor.OzetMesaj}";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "HesapKontrol analiz hatası");
            TempData["Error"] = $"❌ Analiz hatası: {ex.Message}";
        }

        return RedirectToAction("Index");
    }

    // ─── D1b: Tarih Bazlı Gelişmiş Sorgu ("Zaman Makinesi") ───

    [HttpGet]
    public async Task<IActionResult> QueryDate(string tarih, CancellationToken ct)
    {
        if (!DateOnly.TryParse(tarih, out var queryDate))
        {
            TempData["Error"] = "❌ Geçersiz tarih formatı.";
            return RedirectToAction("Index");
        }

        try
        {
            var snapshot = await _service.GetDashboardForDateAsync(queryDate, ct);

            // BUG-7 FIX: SnapshotTarihleri ve AnalizTarihi alanlarını doldur
            List<DateOnly> snapshotTarihleri = new();
            try
            {
                snapshotTarihleri = await _snapshots.GetAllSnapshotDatesAsync(
                    KasaManager.Domain.Reports.KasaRaporTuru.Genel, ct);
            }
            catch (Exception ex2) { _log.LogDebug(ex2, "QueryDate: Snapshot tarihleri alınamadı"); }

            var vm = new HesapKontrolViewModel
            {
                Dashboard = snapshot.Dashboard,
                AcikKayitlar = snapshot.AcikKayitlar,
                TakipteKayitlar = snapshot.TakipteKayitlar,
                GecmisKayitlar = snapshot.OnaylananKayitlar
                    .Concat(snapshot.CozulenKayitlar)
                    .Concat(snapshot.IptalKayitlar)
                    .OrderByDescending(x => x.OlusturmaTarihi)
                    .ToList(),
                ActiveTab = "ozet",
                FilterBaslangic = queryDate,
                FilterBitis = queryDate,
                LastSnapshotDate = queryDate,
                AnalizTarihi = queryDate,
                SnapshotTarihleri = snapshotTarihleri,
                Arama = ""
            };

            TempData["Info"] = snapshot.OzetMesaj;
            ViewData["QueryDate"] = queryDate;
            ViewData["QueryAutoFill"] = snapshot.AutoFill;
            return View("Index", vm);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "HesapKontrol tarih sorgusu hatası: {Tarih}", queryDate);
            TempData["Error"] = $"❌ Sorgu hatası: {ex.Message}";
            return RedirectToAction("Index");
        }
    }

    // ─── D3: PDF Export ───

    [HttpGet]
    public async Task<IActionResult> ExportPdf(
        string? baslangic, string? bitis,
        BankaHesapTuru? hesapTuru, KayitDurumu? durum,
        CancellationToken ct)
    {
        var startDate = DateOnly.TryParse(baslangic, out var ps) ? ps : DateOnly.FromDateTime(DateTime.Now.AddDays(-30));
        var endDate = DateOnly.TryParse(bitis, out var pe) ? pe : DateOnly.FromDateTime(DateTime.Now);

        var dashboard = await _service.GetDashboardAsync(ct: ct);
        var acik = await _service.GetOpenItemsAsync(hesapTuru: hesapTuru, ct: ct);
        var gecmis = await _service.GetHistoryAsync(startDate, endDate, hesapTuru, durum, ct);

        var bytes = await _export.ExportToPdfAsync(dashboard, acik, gecmis, endDate, ct);

        return File(bytes, "application/pdf",
            $"hesap-kontrol-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.pdf");
    }

    // ─── D4: CSV Export ───

    [HttpGet]
    public async Task<IActionResult> ExportCsv(
        string? baslangic, string? bitis,
        BankaHesapTuru? hesapTuru, KayitDurumu? durum,
        CancellationToken ct)
    {
        var startDate = DateOnly.TryParse(baslangic, out var s) ? s : DateOnly.FromDateTime(DateTime.Now.AddDays(-30));
        var endDate = DateOnly.TryParse(bitis, out var e) ? e : DateOnly.FromDateTime(DateTime.Now);

        var kayitlar = await _service.GetHistoryAsync(startDate, endDate, hesapTuru, durum, ct);

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("AnalizTarihi;HesapTuru;Yon;Tutar;Sinif;Durum;Aciklama;DosyaNo;BirimAdi;TespitEdilenTip;Notlar");

        foreach (var k in kayitlar)
        {
            csv.AppendLine(string.Join(";",
                k.AnalizTarihi.ToString("yyyy-MM-dd"),
                k.HesapTuru,
                k.Yon,
                k.Tutar.ToString("N2"),
                k.Sinif,
                k.Durum,
                Escape(k.Aciklama),
                Escape(k.DosyaNo),
                Escape(k.BirimAdi),
                Escape(k.TespitEdilenTip),
                Escape(k.Notlar)));
        }

        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(csv.ToString())).ToArray();

        return File(bytes, "text/csv; charset=utf-8",
            $"hesap-kontrol-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.csv");
    }

    /// <summary>
    /// Escapes a field value for semicolon-delimited CSV per RFC 4180 principles.
    /// </summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var s = value.Replace("\r", "").Replace("\n", " ");
        if (s.Contains(';') || s.Contains('"'))
        {
            s = "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        return s;
    }

    // ─── BUG-1 FIX: Upload klasörü dosya değişiklik hash'i ───
    /// <summary>
    /// Upload klasöründeki dosyaların LastWriteTime değerlerinden
    /// bileşik bir hash üretir. Dosya değişikliği yoksa aynı hash döner.
    /// </summary>
    private static string ComputeUploadFolderHash(string uploadFolder)
    {
        if (!System.IO.Directory.Exists(uploadFolder))
            return "EMPTY";

        var files = System.IO.Directory.GetFiles(uploadFolder, "*.xls*")
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
            return "EMPTY";

        var sb = new System.Text.StringBuilder();
        foreach (var f in files)
        {
            var info = new System.IO.FileInfo(f);
            sb.Append(info.Name).Append('|')
              .Append(info.LastWriteTimeUtc.Ticks).Append('|')
              .Append(info.Length).Append(';');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Analiz cache'ini geçersiz kılar (dosya yükleme veya RunAnalysis sonrası).
    /// </summary>
    internal static void InvalidateAnalysisCache()
    {
        lock (_analysisLock)
        {
            _lastAnalysisFileHash = null;
            _lastAnalysisTarih = null;
        }
    }
}
