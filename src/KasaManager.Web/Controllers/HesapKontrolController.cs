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
    private const string DateRangeValidationViewDataKey = "DateRangeValidationError";
    private const string DateRangeValidationMessage =
        "Başlangıç tarihi bitiş tarihinden sonra olamaz ve tarih aralığı seçilen analiz tarihini aşamaz.";

    // FAZ-3: GET yalnız mevcut kayıtları okur; analiz state'i tutulmaz.
    private readonly IBankaHesapKontrolService _service;
    private readonly ICurrentUser _currentUser;

    private readonly IHesapKontrolExportService _export;
    private readonly IFinansalIstisnaService _finansalIstisna;
    private readonly IHesapKontrolSourceResolver _sourceResolver;
    private readonly ILogger<HesapKontrolController> _log;
    private readonly IWebHostEnvironment _env;
    private readonly IManualResolveWriteBusinessDateResolver _writeBusinessDateResolver;

    public HesapKontrolController(
        IBankaHesapKontrolService service,
        ICurrentUser currentUser,
        IHesapKontrolExportService export,
        IFinansalIstisnaService finansalIstisna,
        IHesapKontrolSourceResolver sourceResolver,
        ILogger<HesapKontrolController> log,
        IWebHostEnvironment env,
        IManualResolveWriteBusinessDateResolver writeBusinessDateResolver)
    {
        _service = service;
        _currentUser = currentUser;
        _export = export;
        _finansalIstisna = finansalIstisna;
        _sourceResolver = sourceResolver;
        _log = log;
        _env = env;
        _writeBusinessDateResolver = writeBusinessDateResolver;
    }

    private bool TryResolveInteractiveActor(out int actorUserId, out string? actorUsername)
    {
        try
        {
            actorUserId = _currentUser.RequireAuthenticatedUserId();
            actorUsername = _currentUser.Username;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            actorUserId = default;
            actorUsername = null;
            return false;
        }
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
        // P4.3: Snapshot UI bağları koparıldı.
        // Tarih verilmezse varsayılan olarak BUGÜN baz alınır.
        DateOnly analizTarihi;
        if (DateOnly.TryParse(analizTarihiStr, out var parsed))
            analizTarihi = parsed;
        else
            analizTarihi = DateOnly.FromDateTime(DateTime.Now);

        if (!TryResolveIndexDateRange(
                analizTarihi, baslangic, bitis, out var startDate, out var endDate))
        {
            ViewData[DateRangeValidationViewDataKey] = DateRangeValidationMessage;
            return View(new HesapKontrolViewModel
            {
                ActiveTab = tab ?? "ozet",
                FilterHesapTuru = hesapTuru,
                FilterDurum = durum,
                FilterBaslangic = startDate,
                FilterBitis = endDate,
                Arama = arama ?? "",
                AnalizTarihi = analizTarihi
            });
        }

        // FAZ-3: GET artık salt okunurdur.
        // Excel dosyaları okunmaz, analiz çalıştırılmaz, DB'ye yazılmaz.
        // Analiz yalnızca RunAnalysis POST action'ı üzerinden başlatılır.

        // ── Servis çağrıları — her biri bağımsız try/catch ile korunur ──
        // Herhangi birinin başarısız olması diğerlerini veya sayfanın render'ını engellemez.

        HesapKontrolDashboard? dashboard = null;
        try
        {
            // BUG-SYNC-1 FIX: Dashboard'a da hesapTuru filtresini geçir.
            // ESKİ: GetDashboardAsync(analizTarihi) — hesapTuru parametresi yoktu.
            // Bu, hesapTuru filtreli OpenItems ile uyumsuz sayılar üretiyordu.
            // YENİ: hesapTuru parametresi de geçirilerek Dashboard ve OpenItems aynı
            // filtreleme semantiğini kullanır.
            dashboard = await _service.GetDashboardAsync(analizTarihi, hesapTuru, ct);
        }
        catch (OperationCanceledException) { _log.LogInformation("HesapKontrol dashboard: istek iptal edildi (kullanıcı sayfadan ayrıldı)"); }
        catch (Exception ex) { _log.LogError(ex, "HesapKontrol dashboard verisi alınamadı"); }

        var acikKayitlar = new List<HesapKontrolKaydi>();
        try
        {
            // FIX: Geçmiş günlerin açık kayıtları da gösterilmeli.
            // Eski: baslangic: analizTarihi → sadece o günün kayıtlarını getiriyordu.
            // Yeni: baslangic kaldırıldı → tüm geçmiş açık kayıtlar dahil (bitis ile üst sınır korunur).
            acikKayitlar = await _service.GetOpenItemsAsync(
                hesapTuru: hesapTuru, bitis: analizTarihi, ct: ct);
        }
        catch (OperationCanceledException) { _log.LogInformation("HesapKontrol açık kayıtlar: istek iptal edildi"); }
        catch (Exception ex) { _log.LogError(ex, "HesapKontrol açık kayıtlar alınamadı"); }

        var takipteKayitlar = new List<HesapKontrolKaydi>();
        try
        {
            takipteKayitlar = await _service.GetTrackedItemsAsync(
                hesapTuru: hesapTuru, analizTarihi: analizTarihi, ct: ct);
        }
        catch (OperationCanceledException) { _log.LogInformation("HesapKontrol takipte kayıtlar: istek iptal edildi"); }
        catch (Exception ex) { _log.LogError(ex, "HesapKontrol takipte kayıtlar alınamadı"); }

        // Geçmiş verilerini HER ZAMAN yükle — tarih aralığında TÜM durumları getir
        var gecmis = new List<HesapKontrolKaydi>();
        try
        {
            gecmis = await _service.GetHistoryAsync(startDate, endDate, hesapTuru, durum, ct);
        }
        catch (OperationCanceledException) { _log.LogInformation("HesapKontrol geçmiş kayıtlar: istek iptal edildi"); }
        catch (Exception ex) { _log.LogError(ex, "HesapKontrol geçmiş kayıtlar alınamadı"); }

        // Takip yaşam döngüsü — veritabanından SADECE takibe alınmış kayıtları getir
        var takipGecmisi = new List<HesapKontrolKaydi>();
        if (tab == "takipte")
        {
            try
            {
                takipGecmisi = await _service.GetTrackingLifecycleAsync(
                    startDate, endDate, hesapTuru, takipDurum, ct);
            }
            catch (OperationCanceledException) { _log.LogInformation("HesapKontrol takip geçmişi: istek iptal edildi"); }
            catch (Exception ex) { _log.LogError(ex, "HesapKontrol takip geçmişi alınamadı"); }
        }

        // Takip özeti — HER ZAMAN yükle (Dashboard + Takipte sekmesi)
        TakipOzeti? takipOzeti = null;
        try
        {
            takipOzeti = await _service.GetTrackingSummaryAsync(analizTarihi, ct);
        }
        catch (OperationCanceledException) { _log.LogInformation("HesapKontrol takip özeti: istek iptal edildi"); }
        catch (Exception ex) { _log.LogError(ex, "HesapKontrol takip özeti alınamadı"); }

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
            AnalizTarihi = analizTarihi
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
    public async Task<IActionResult> Confirm(Guid id, string? not, string? tarih = null)
    {
        if (!TryResolveInteractiveActor(out var actorUserId, out var actorUsername))
            return Unauthorized();

        var result = await _service.ConfirmMatchAsync(id, actorUserId, actorUsername, not);

        if (result)
            TempData["Info"] = "✅ Kayıt başarıyla onaylandı.";
        else
            TempData["Error"] = "❌ Kayıt onaylanamadı. Kayıt bulunamadı veya zaten kapatılmış.";

        // BUG-DATE-2 FIX: Status change — cache invalidation kaldırıldı.
        if (!string.IsNullOrEmpty(tarih)) return RedirectToAction("Index", new { tab = "acik", analizTarihiStr = tarih });
        return RedirectToAction("Index", new { tab = "acik" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, string? sebep, string? tarih = null)
    {
        if (!TryResolveInteractiveActor(out var actorUserId, out var actorUsername))
            return Unauthorized();

        var result = await _service.CancelAsync(id, actorUserId, actorUsername, sebep);

        if (result)
            TempData["Info"] = "🚫 Kayıt iptal edildi.";
        else
            TempData["Error"] = "❌ Kayıt iptal edilemedi. Kayıt bulunamadı veya zaten kapatılmış.";

        // BUG-DATE-2 FIX: Status change ile cache invalidation ayrıldı.
        // Kayıt iptal etmek dosya değişikliği değil — cache geçersizleme gereksiz ve re-analysis tetikler.
        if (!string.IsNullOrEmpty(tarih)) return RedirectToAction("Index", new { tab = "acik", analizTarihiStr = tarih });
        return RedirectToAction("Index", new { tab = "acik" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkCancel(List<Guid> ids, string? tarih = null)
    {
        if (!TryResolveInteractiveActor(out var actorUserId, out var actorUsername))
            return Unauthorized();

        var count = 0;

        foreach (var id in ids)
        {
            var result = await _service.CancelAsync(id, actorUserId, actorUsername, "Toplu yok say");
            if (result) count++;
        }

        TempData["Info"] = $"🚫 {count} kayıt toplu olarak yok sayıldı.";
        // BUG-DATE-2 FIX: Status change — cache invalidation kaldırıldı (dosya değişmedi).
        if (!string.IsNullOrEmpty(tarih)) return RedirectToAction("Index", new { tab = "acik", analizTarihiStr = tarih });
        return RedirectToAction("Index", new { tab = "acik" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDismissStale(int gunSiniri = 2, CancellationToken ct = default)
    {
        if (!TryResolveInteractiveActor(out var actorUserId, out var actorUsername))
            return Unauthorized();

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
            var result = await _service.CancelAsync(k.Id, actorUserId, actorUsername, $"Otomatik temizlik: {(bugun.DayNumber - k.AnalizTarihi.DayNumber)} günlük stale kayıt");
            if (result) count++;
        }

        TempData["Info"] = count > 0
            ? $"🧹 {count} eski açık kayıt temizlendi ({gunSiniri}+ gün öncesi)."
            : "✅ Temizlenecek eski kayıt bulunamadı.";

        // BUG-DATE-2 FIX: Status-only change — dosya değişmedi, cache invalidation kaldırıldı.
        // Aksi halde redirect sonrası auto-analysis tetiklenir ve iptal edilen kayıtlar
        // karşılaştırma dosyalarından tekrar oluşturulur.
        return RedirectToAction("Index", new { tab = "acik" });
    }

    // ─── Takip İşlemleri ───

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartTracking(Guid id, string? not, string? tarih = null)
    {
        if (!TryResolveInteractiveActor(out var actorUserId, out var actorUsername))
            return Unauthorized();

        var result = await _service.StartTrackingAsync(id, actorUserId, actorUsername, not);

        if (result)
            TempData["Info"] = "📌 Kayıt takibe alındı. Takipte sekmesinden izleyebilirsiniz.";
        else
            TempData["Error"] = "❌ Kayıt takibe alınamadı. Kayıt bulunamadı veya zaten işlem görmüş.";

        // BUG-DATE-2 FIX: Status change — cache invalidation kaldırıldı.
        if (!string.IsNullOrEmpty(tarih)) return RedirectToAction("Index", new { tab = "acik", analizTarihiStr = tarih });
        return RedirectToAction("Index", new { tab = "acik" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(Guid id, string? not, string? tarih = null)
    {
        if (!TryResolveInteractiveActor(out var actorUserId, out var actorUsername))
            return Unauthorized();

        var targetKind = await _service.GetResolveTargetKindAsync(id);

        bool result;
        switch (targetKind)
        {
            case HesapKontrolResolveTargetKind.Stopaj:
                result = await _service.ResolveTrackedStopajAsync(id, actorUserId, actorUsername, not);
                break;

            case HesapKontrolResolveTargetKind.Financial:
                var baseFolder = Path.Combine(_env.WebRootPath, "Data", "Raporlar");
                var writeDate = await _writeBusinessDateResolver.ResolveAsync(baseFolder);
                if (!writeDate.Success)
                {
                    _log.LogWarning(
                        "[HK-RESOLVE-WRITE-DATE-FAIL-CLOSED] KayitId={KayitId} Reason={Reason} Detail={Detail}",
                        id, writeDate.FailureReason, writeDate.Detail);
                    TempData["Error"] = $"❌ Kayıt çözülemedi. Finansal işlem günü doğrulanamadı: {writeDate.Detail}";
                    if (!string.IsNullOrEmpty(tarih)) return RedirectToAction("Index", new { tab = "takipte", analizTarihiStr = tarih });
                    return RedirectToAction("Index", new { tab = "takipte" });
                }

                result = await _service.ResolveTrackedFinancialAsync(
                    id, writeDate.BusinessDate!.Value, actorUserId, actorUsername, not);
                break;

            default:
                result = false;
                break;
        }

        if (result)
            TempData["Info"] = "✅ Takipteki kayıt çözüldü olarak işaretlendi.";
        else
            TempData["Error"] = "❌ Kayıt çözülemedi. Kayıt takipte değil.";

        // BUG-DATE-2 FIX: Status change — cache invalidation kaldırıldı.
        if (!string.IsNullOrEmpty(tarih)) return RedirectToAction("Index", new { tab = "takipte", analizTarihiStr = tarih });
        return RedirectToAction("Index", new { tab = "takipte" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revert(Guid id, string? sebep, string? returnTab, string? tarih = null)
    {
        if (!TryResolveInteractiveActor(out var actorUserId, out var actorUsername))
            return Unauthorized();

        var result = await _service.RevertAsync(id, actorUserId, actorUsername, sebep);

        if (result)
            TempData["Info"] = "↩ Kayıt geri alındı ve Açık Kayıtlar'a döndü.";
        else
            TempData["Error"] = "❌ Kayıt geri alınamadı. Kayıt zaten Açık durumda.";

        // BUG-DATE-2 FIX: Status change — cache invalidation kaldırıldı.
        var hedefTab = returnTab ?? "acik";
        if (!string.IsNullOrEmpty(tarih)) return RedirectToAction("Index", new { tab = hedefTab, analizTarihiStr = tarih });
        return RedirectToAction("Index", new { tab = hedefTab });
    }

    // ─── CrossDay Kısmi Eşleşme Onay/Red ───

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApprovePotentialMatch(Guid eksikId, Guid fazlaId, string? tarih = null)
    {
        if (!TryResolveInteractiveActor(out var actorUserId, out var actorUsername))
            return Unauthorized();

        var result = await _service.ApprovePotentialMatchAsync(eksikId, fazlaId, actorUserId, actorUsername);

        if (result)
            TempData["Info"] = "✅ Kısmi eşleşme onaylandı — kayıtlar çözüldü olarak işaretlendi.";
        else
            TempData["Error"] = "❌ Eşleşme onaylanamadı. Kayıtlar bulunamadı veya işlem görmüş.";

        // BUG-DATE-2 FIX: Status change — cache invalidation kaldırıldı.
        if (!string.IsNullOrEmpty(tarih)) return RedirectToAction("Index", new { tab = "takipte", analizTarihiStr = tarih });
        return RedirectToAction("Index", new { tab = "takipte" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectPotentialMatch(Guid eksikId, Guid fazlaId, string? tarih = null)
    {
        if (!TryResolveInteractiveActor(out var actorUserId, out var actorUsername))
            return Unauthorized();

        var result = await _service.RejectPotentialMatchAsync(eksikId, fazlaId, actorUserId, actorUsername);

        if (result)
            TempData["Info"] = "❌ Kısmi eşleşme reddedildi — kayıt takipte kalmaya devam ediyor.";
        else
            TempData["Error"] = "❌ İşlem başarısız. Kayıt bulunamadı.";

        // BUG-DATE-2 FIX: Status change — cache invalidation kaldırıldı.
        if (!string.IsNullOrEmpty(tarih)) return RedirectToAction("Index", new { tab = "takipte", analizTarihiStr = tarih });
        return RedirectToAction("Index", new { tab = "takipte" });
    }

    // ─── Faz 3: HesapKontrol → Finansal İstisna Oluşturma ───

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFinansalIstisnaFromHK(
        Guid kayitId, int hesapTuru, int yon, decimal tutar,
        string? aciklama, string? analizTarihi, CancellationToken ct)
    {
        if (!TryResolveInteractiveActor(out _, out var actorUsername))
            return Unauthorized();

        try
        {
            var tarih = DateOnly.TryParse(analizTarihi, out var d) ? d : DateOnly.FromDateTime(DateTime.Today);
            var kullanici = actorUsername ?? "Anonim";

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
            TempData["Error"] = "❌ İstisna oluşturulamadı. Lütfen tekrar deneyin.";
        }

        // BUG-DATE-2 FIX: Status change — cache invalidation kaldırıldı.
        var t = DateOnly.TryParse(analizTarihi, out var pd) ? pd.ToString("yyyy-MM-dd") : null;
        if (!string.IsNullOrEmpty(t)) return RedirectToAction("Index", new { tab = "acik", analizTarihiStr = t });
        return RedirectToAction("Index", new { tab = "acik" });
    }

    // ─── D1: Geriye Dönük Analiz ───

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunAnalysis(string? tarih, CancellationToken ct)
    {
        if (!TryResolveInteractiveActor(out var actorUserId, out _))
            return Unauthorized();

        // Formdan gelen tarihi parse et, yoksa bugünü kullan
        DateOnly analizTarihi;
        if (DateOnly.TryParse(tarih, out var d))
        {
            analizTarihi = d;
        }
        else
        {
            analizTarihi = DateOnly.FromDateTime(DateTime.Now);
        }

        var baseFolder = Path.Combine(_env.WebRootPath, "Data", "Raporlar");
        var source = _sourceResolver.Resolve(baseFolder, analizTarihi);
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
                "[HK-RUNANALYSIS] Geçerli kaynak bulunamadı. Tarih={Date} Detay={Detail}",
                analizTarihi, source.TechnicalError);
            TempData["Error"] =
                "❌ Seçilen tarih için gerekli Excel kaynakları doğrulanamadı. " +
                "Eksik veya geçersiz dosyaları kontrol ederek tekrar deneyin.";
            return RedirectToAction("Index", new { analizTarihiStr = analizTarihi.ToString("yyyy-MM-dd") });
        }

        try
        {
            var rapor = await _service.AnalyzeFromComparisonAsync(analizTarihi, source.Folder!, actorUserId, ct);
            TempData["Info"] = $"✅ Analiz tamamlandı: {rapor.OzetMesaj}";

            if (rapor.CrossDayEslesmeler.Count > 0)
            {
                var toplamTutar = rapor.CrossDayEslesmeler.Sum(x => x.Tutar);
                TempData["CrossDay"] = $"✅ Takipteki {rapor.CrossDayEslesmeler.Count} kayıt çözüldü! (Toplam: {toplamTutar:N2} ₺)";
            }
            if (rapor.PotansiyelEslesmeler.Count > 0)
            {
                var potTutar = rapor.PotansiyelEslesmeler.Sum(x => x.Tutar);
                TempData["CrossDayPotansiyel"] = $"⚠️ {rapor.PotansiyelEslesmeler.Count} kısmi eşleşme onayınızı bekliyor ({potTutar:N2} ₺)";
                TempData["PotansiyelEslesmelerJson"] = System.Text.Json.JsonSerializer.Serialize(rapor.PotansiyelEslesmeler);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "HesapKontrol analiz hatası");
            TempData["Error"] = "❌ Analiz tamamlanamadı. Lütfen tekrar deneyin.";
        }

        return RedirectToAction("Index", new { analizTarihiStr = analizTarihi.ToString("yyyy-MM-dd") });
    }

    // ─── D1b: Tarih Bazlı Gelişmiş Sorgu ("Zaman Makinesi") ───

    [HttpGet]
    public async Task<IActionResult> QueryDate(string tarih, string? tab, CancellationToken ct)
    {
        if (!DateOnly.TryParse(tarih, out var queryDate))
        {
            return BadRequest("Geçersiz tarih formatı. Lütfen 'yyyy-MM-dd' formatında geçerli bir tarih gönderin.");
        }

        try
        {
            var snapshot = await _service.GetDashboardForDateAsync(queryDate, ct);
            _log.LogInformation(
                "[HK-QUERYDATE-SNAPSHOT] Date={Date} Total={Total} Open={Open} Tracking={Tracking} Cancelled={Cancelled} Resolved={Resolved} Processed={Processed}",
                queryDate,
                snapshot.Summary.TotalCount,
                snapshot.Summary.AcikCount,
                snapshot.Summary.TakipteCount,
                snapshot.Summary.IptalCount,
                snapshot.Summary.CozulduCount,
                snapshot.Summary.ProcessedCount);

            // P4.3 Snapshot query kaldırıldı
            DateOnly? realLastSnapshot = DateOnly.FromDateTime(DateTime.Now);

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
                SnapshotSummary = snapshot.Summary,
                ActiveTab = tab ?? "ozet",
                FilterBaslangic = queryDate,
                FilterBitis = queryDate,
                AnalizTarihi = queryDate,
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
            TempData["Error"] = "❌ Sorgu tamamlanamadı. Lütfen tekrar deneyin.";
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

    private static bool TryResolveIndexDateRange(
        DateOnly analizTarihi,
        string? baslangic,
        string? bitis,
        out DateOnly startDate,
        out DateOnly endDate)
    {
        var hasStart = DateOnly.TryParse(baslangic, out var parsedStart);
        var hasEnd = DateOnly.TryParse(bitis, out var parsedEnd);

        startDate = hasStart
            ? parsedStart
            : hasEnd
                ? parsedEnd.AddDays(-7)
                : analizTarihi.AddDays(-7);
        endDate = hasEnd ? parsedEnd : analizTarihi;

        return startDate <= endDate
            && startDate <= analizTarihi
            && endDate <= analizTarihi;
    }

}
