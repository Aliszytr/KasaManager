#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Validation;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// Kural tabanlı doğrulama servisi.
/// Hesaplama sonuçlarını analiz eder, tutarsızlıkları tespit eder.
/// Dismiss (Çözüldü/Tamamlandı) desteği sağlar.
/// </summary>
public sealed class KasaValidationService : IKasaValidationService
{
    private readonly KasaManagerDbContext _db;
    private readonly ILogger<KasaValidationService> _logger;

    // Eşik değerleri (gelecekte ayarlardan alınabilir)
    private const decimal MutabakatFarkEsik = 100m;
    private const decimal BuyukEksikFazlaEsik = 500m;
    private const decimal StopajTolerans = 0.01m;

    public KasaValidationService(
        KasaManagerDbContext db,
        ILogger<KasaValidationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public List<KasaValidationResult> Validate(KasaRaporData data)
    {
        var results = new List<KasaValidationResult>();

        // ═══ KURAL 1: Mutabakat Farkı Yüksek ═══
        // GenelKasa'nın beklenen ile arasındaki fark
        var mutabakatFark = Math.Abs(data.StopajKontrolFark);
        if (!data.StopajKontrolOk)
        {
            results.Add(new KasaValidationResult(
                ValidationSeverity.Error,
                "STOPAJ_KONTROL_FAIL",
                $"Stopaj kontrol başarısız! Fark: {mutabakatFark:N2} ₺ (Reddiyat − BankadanÇıkan − Stopaj ≠ 0)",
                "stopaj_kontrol",
                mutabakatFark));
        }

        // ═══ KURAL 2: Negatif Vergide Biriken ═══
        if (data.VergideBirikenKasa < 0)
        {
            results.Add(new KasaValidationResult(
                ValidationSeverity.Warning,
                "NEGATIF_VERGI_BIRIKEN",
                $"Vergide Biriken Kasa negatif: {data.VergideBirikenKasa:N2} ₺. Seed değerini kontrol edin.",
                "vergide_biriken_kasa",
                data.VergideBirikenKasa));
        }

        // ═══ KURAL 3: Negatif Genel Kasa ═══
        if (data.GenelKasa < 0)
        {
            results.Add(new KasaValidationResult(
                ValidationSeverity.Warning,
                "NEGATIF_KASA",
                $"Genel Kasa negatif: {data.GenelKasa:N2} ₺. Devreden kasa veya giriş değerlerini kontrol edin.",
                "genel_kasa",
                data.GenelKasa));
        }

        // ═══ KURAL 4: Bankaya Toplam = 0 Kontrolü ═══
        // Bankaya hiçbir şey gönderilmiyorsa anormal
        if (data.BankayaToplam == 0 && data.GenelKasa != 0)
        {
            results.Add(new KasaValidationResult(
                ValidationSeverity.Info,
                "BANKA_TOPLAM_SIFIR",
                "Bankaya yatırılacak toplam tutar 0 ₺. Formül hesaplamalarını kontrol edin.",
                "bankaya_toplam"));
        }

        // ═══ KURAL 5: Büyük Eksik/Fazla Tahsilat ═══
        var eksikFazlaTahsilat = Math.Abs(data.GuneAitEksikFazlaTahsilat);
        if (eksikFazlaTahsilat > BuyukEksikFazlaEsik)
        {
            results.Add(new KasaValidationResult(
                ValidationSeverity.Warning,
                "BUYUK_EKSIK_FAZLA_TAHSILAT",
                $"Güne ait eksik/fazla tahsilat yüksek: {data.GuneAitEksikFazlaTahsilat:N2} ₺. HesapKontrol modülünden detay kontrol edin.",
                "gune_ait_eksik_fazla_tahsilat",
                data.GuneAitEksikFazlaTahsilat));
        }

        // ═══ KURAL 6: Büyük Eksik/Fazla Harç ═══
        var eksikFazlaHarc = Math.Abs(data.GuneAitEksikFazlaHarc);
        if (eksikFazlaHarc > BuyukEksikFazlaEsik)
        {
            results.Add(new KasaValidationResult(
                ValidationSeverity.Warning,
                "BUYUK_EKSIK_FAZLA_HARC",
                $"Güne ait eksik/fazla harç yüksek: {data.GuneAitEksikFazlaHarc:N2} ₺. HesapKontrol modülünden detay kontrol edin.",
                "gune_ait_eksik_fazla_harc",
                data.GuneAitEksikFazlaHarc));
        }

        // ═══ KURAL 7: Dünden Kalan Açık Eksik/Fazla ═══
        var dundenTahsilat = Math.Abs(data.DundenEksikFazlaTahsilat);
        var dundenHarc = Math.Abs(data.DundenEksikFazlaHarc);
        if (dundenTahsilat > 0 || dundenHarc > 0)
        {
            var toplamDunden = dundenTahsilat + dundenHarc;
            results.Add(new KasaValidationResult(
                ValidationSeverity.Info,
                "DUNDEN_ACIK_EKSIK_FAZLA",
                $"Önceki günlerden kalan açık eksik/fazla: Tahsilat {data.DundenEksikFazlaTahsilat:N2} ₺, Harç {data.DundenEksikFazlaHarc:N2} ₺",
                "dunden_eksik_fazla",
                toplamDunden));
        }

        // ═══ KURAL 8: Banka Bakiye Kontrolü ═══
        // YarinaDevredecekBanka negatif ise sorun var
        if (data.YarinaDevredecekBanka < 0)
        {
            results.Add(new KasaValidationResult(
                ValidationSeverity.Error,
                "BANKA_DEVIR_NEGATIF",
                $"Yarına devredecek banka negatif: {data.YarinaDevredecekBanka:N2} ₺. Banka giriş/çıkış değerlerini kontrol edin.",
                "yarina_devredecek_banka",
                data.YarinaDevredecekBanka));
        }

        // ═══ KURAL 9: Beklenmeyen Banka Girişi — Tahsilat ═══
        var beklenenGirisTahsilat = data.EftOtomatikIade + data.GelenHavale
                                   + data.IadeKelimesiGiris + data.DundenEksikFazlaGelenTahsilat;
        var muhFarkTahsilat = data.BankaGirenTahsilat
            - data.BankayaTahsilat - data.OnlineTahsilat
            - beklenenGirisTahsilat;
        if (data.BankaGirenTahsilat != 0 && Math.Abs(muhFarkTahsilat) > StopajTolerans)
        {
            // olağan dışı fark = beklenen girişler hariç ham fark
            var olaganDisiFark = data.BankaGirenTahsilat - data.BankayaTahsilat - data.OnlineTahsilat;
            var sev = Math.Abs(muhFarkTahsilat) > BuyukEksikFazlaEsik
                ? ValidationSeverity.Error : ValidationSeverity.Warning;

            // Beklenen girişler detayı
            var beklenenDetay = new List<string>();
            if (data.EftOtomatikIade > 0) beklenenDetay.Add($"EFT İade: {data.EftOtomatikIade:N2} ₺");
            if (data.GelenHavale > 0) beklenenDetay.Add($"Gelen Havale: {data.GelenHavale:N2} ₺");
            if (data.IadeKelimesiGiris > 0) beklenenDetay.Add($"İade Kelimesi: {data.IadeKelimesiGiris:N2} ₺");
            if (data.DundenEksikFazlaGelenTahsilat > 0) beklenenDetay.Add($"Önceki Günden Gelen: {data.DundenEksikFazlaGelenTahsilat:N2} ₺");

            var mesaj = $"Banka tahsilat mutabakatında toplam {muhFarkTahsilat:N2} ₺ fark.";
            if (beklenenDetay.Count > 0)
                mesaj += $" Beklenen girişler → {string.Join(" • ", beklenenDetay)}.";
            if (Math.Abs(olaganDisiFark) > StopajTolerans)
            {
                var yonStr = olaganDisiFark < 0 ? "Eksik" : "Fazla";
                mesaj += $" ⚠️ Olağan Dışı (Beklenmeyen) fark: {olaganDisiFark:N2} ₺ ({yonStr}) — Bu değeri Hesap Kontrol'de takibe alın!";
            }
            else if (beklenenDetay.Count > 0)
            {
                mesaj += " Fark tamamen beklenen girişlerden kaynaklanmaktadır.";
            }

            results.Add(new KasaValidationResult(
                sev,
                "BEKLENMEYEN_BANKA_GIRIS_TAHSILAT",
                mesaj,
                "muhasebe_fark_tahsilat",
                muhFarkTahsilat));
        }

        // ═══ KURAL 10: Beklenmeyen Banka Girişi — Harç ═══
        var beklenenGirisHarc = data.DundenEksikFazlaGelenHarc;
        var muhFarkHarc = data.BankaGirenHarc
            - data.BankayaHarc - data.OnlineHarc
            - beklenenGirisHarc;
        if (data.BankaGirenHarc != 0 && Math.Abs(muhFarkHarc) > StopajTolerans)
        {
            var olaganDisiFarkH = data.BankaGirenHarc - data.BankayaHarc - data.OnlineHarc;
            var sev = Math.Abs(muhFarkHarc) > BuyukEksikFazlaEsik
                ? ValidationSeverity.Error : ValidationSeverity.Warning;

            var mesajH = $"Banka harç mutabakatında toplam {muhFarkHarc:N2} ₺ fark.";
            if (beklenenGirisHarc > 0)
                mesajH += $" Beklenen → Önceki Günden Gelen: {beklenenGirisHarc:N2} ₺.";
            if (Math.Abs(olaganDisiFarkH) > StopajTolerans)
            {
                var yonStrH = olaganDisiFarkH < 0 ? "Eksik" : "Fazla";
                mesajH += $" ⚠️ Olağan Dışı fark: {olaganDisiFarkH:N2} ₺ ({yonStrH}) — Bu değeri takibe alın!";
            }
            else if (beklenenGirisHarc > 0)
            {
                mesajH += " Fark tamamen beklenen girişlerden kaynaklanmaktadır.";
            }

            results.Add(new KasaValidationResult(
                sev,
                "BEKLENMEYEN_BANKA_GIRIS_HARC",
                mesajH,
                "muhasebe_fark_harc",
                muhFarkHarc));
        }

        // ═══ KURAL 11: UYAP Girişi Gerekli ═══
        var toplamIadeler = data.EftOtomatikIade + data.GelenHavale + data.IadeKelimesiGiris;
        if (toplamIadeler > StopajTolerans)
        {
            var detaylar = new List<string>();
            if (data.EftOtomatikIade > 0) detaylar.Add($"EFT Otomatik İade: {data.EftOtomatikIade:N2} ₺");
            if (data.GelenHavale > 0) detaylar.Add($"Gelen Havale: {data.GelenHavale:N2} ₺");
            if (data.IadeKelimesiGiris > 0) detaylar.Add($"İade Kelimesi Giriş: {data.IadeKelimesiGiris:N2} ₺");
            results.Add(new KasaValidationResult(
                ValidationSeverity.Info,
                "UYAP_GIRIS_GEREKLI",
                $"Toplam {toplamIadeler:N2} ₺ iade bankaya girdi ({string.Join(", ", detaylar)}). " +
                "Çıktığı birime UYAP sistemine girişi yapılmalıdır.",
                "eft_otomatik_iade",
                toplamIadeler));
        }

        // ═══ KURAL 12: Bankaya Yatırılacak Konsolide Kontrol (Sabah Kasa) ═══
        // Karttaki 3 değerin banka dosyalarındaki fiili yatırma işlemleriyle karşılaştırılması
        if (data.IsSabahKasa)
        {
            var hatalar = new List<string>();
            bool tahsilatKontrolEdildi = false, harcKontrolEdildi = false, stopajKontrolEdildi = false;

            // 1) Tahsilat: Kart değeri vs BankaTahsilat.xlsx "Mevduata Para Yatırma" toplamı
            if (data.BankayaTahsilat != 0)
            {
                tahsilatKontrolEdildi = true;
                var fark = data.BankayaTahsilat - data.BankaMevduatTahsilat;
                if (Math.Abs(fark) > StopajTolerans)
                {
                    if (data.BankaMevduatTahsilat == 0)
                        hatalar.Add($"Tahsilat ({data.BankayaTahsilat:N2} ₺): BankaTahsilat.xlsx dosyasında Mevduata Para Yatırma kaydı bulunamadı.");
                    else
                        hatalar.Add($"Tahsilat ({data.BankayaTahsilat:N2} ₺): Bankada bulunan Mevduata Para Yatırma toplamı {data.BankaMevduatTahsilat:N2} ₺ — Fark: {fark:N2} ₺");
                }
            }

            // 2) Harç: Kart değeri vs BankaHarc.xlsx "Mevduata Para Yatırma" toplamı
            if (data.BankayaHarc != 0)
            {
                harcKontrolEdildi = true;
                var fark = data.BankayaHarc - data.BankaMevduatHarc;
                if (Math.Abs(fark) > StopajTolerans)
                {
                    if (data.BankaMevduatHarc == 0)
                        hatalar.Add($"Harç ({data.BankayaHarc:N2} ₺): BankaHarc.xlsx dosyasında Mevduata Para Yatırma kaydı bulunamadı.");
                    else
                        hatalar.Add($"Harç ({data.BankayaHarc:N2} ₺): Bankada bulunan Mevduata Para Yatırma toplamı {data.BankaMevduatHarc:N2} ₺ — Fark: {fark:N2} ₺");
                }
            }

            // 3) Stopaj: Kart değeri vs BankaTahsilat.xlsx "Virman" toplamı
            if (data.BankayaStopaj != 0)
            {
                stopajKontrolEdildi = true;
                var fark = data.BankayaStopaj - data.BankaVirmanTahsilat;
                if (Math.Abs(fark) > StopajTolerans)
                {
                    if (data.BankaVirmanTahsilat == 0)
                        hatalar.Add($"Stopaj/Virman ({data.BankayaStopaj:N2} ₺): BankaTahsilat.xlsx dosyasında Virman kaydı bulunamadı.");
                    else
                        hatalar.Add($"Stopaj/Virman ({data.BankayaStopaj:N2} ₺): Bankada bulunan Virman toplamı {data.BankaVirmanTahsilat:N2} ₺ — Fark: {fark:N2} ₺");
                }
            }

            // Konsolide sonuç
            if (tahsilatKontrolEdildi || harcKontrolEdildi || stopajKontrolEdildi)
            {
                if (hatalar.Count == 0)
                {
                    results.Add(new KasaValidationResult(
                        ValidationSeverity.Info,
                        "BANKAYA_YATIRILACAK_KONTROL",
                        "✅ Bankaya yatırılması planlanan değerler doğru bir şekilde yatırılmıştır.",
                        "bankaya_yatirilacak_toplam",
                        0m));
                }
                else
                {
                    var mesaj = "Bankaya yatırılması gereken değerlerden aşağıdaki hata(lar) tespit edilmiştir:\n• " +
                                string.Join("\n• ", hatalar);
                    results.Add(new KasaValidationResult(
                        ValidationSeverity.Warning,
                        "BANKAYA_YATIRILACAK_KONTROL",
                        mesaj,
                        "bankaya_yatirilacak_toplam",
                        hatalar.Count));
                }
            }
        }

        if (results.Count > 0)
        {
            _logger.LogInformation("Validation: {Count} kural tetiklendi — {Codes}",
                results.Count, string.Join(", ", results.Select(r => r.Code)));
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task DismissAsync(
        DateOnly raporTarihi,
        string kasaTuru,
        string ruleCode,
        string? note = null,
        string? user = null,
        CancellationToken ct = default)
    {
        // Aynı gün + kasa + kural zaten dismiss edilmiş mi?
        var exists = await _db.DismissedValidations
            .AnyAsync(d => d.RaporTarihi == raporTarihi
                        && d.KasaTuru == kasaTuru
                        && d.RuleCode == ruleCode, ct);

        if (exists)
        {
            _logger.LogDebug("Validation dismiss zaten mevcut: {RuleCode} @ {Tarih}/{Kasa}",
                ruleCode, raporTarihi, kasaTuru);
            return;
        }

        _db.DismissedValidations.Add(new DismissedValidation
        {
            RaporTarihi = raporTarihi,
            KasaTuru = kasaTuru,
            RuleCode = ruleCode,
            DismissedBy = user,
            Note = note,
            DismissedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Validation dismissed: {RuleCode} @ {Tarih}/{Kasa} by {User}",
            ruleCode, raporTarihi, kasaTuru, user ?? "system");
    }

    /// <inheritdoc/>
    public async Task<HashSet<string>> GetDismissedCodesAsync(
        DateOnly raporTarihi,
        string kasaTuru,
        CancellationToken ct = default)
    {
        var codes = await _db.DismissedValidations
            .Where(d => d.RaporTarihi == raporTarihi && d.KasaTuru == kasaTuru)
            .Select(d => d.RuleCode)
            .ToListAsync(ct);

        return new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
    }
}
