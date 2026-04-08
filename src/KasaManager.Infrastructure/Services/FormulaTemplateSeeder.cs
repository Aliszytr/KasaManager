#nullable enable
using KasaManager.Domain.FormulaEngine.Authoring;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// R21: Varsayılan formül şablonlarını veritabanına seed eder.
/// Uygulama başlangıcında çağrılır, eksik şablonları oluşturur.
/// V2: Mevcut yanlış formülleri de otomatik düzeltir (GenelKasa R10 uyumu).
/// </summary>
public interface IFormulaTemplateSeeder
{
    Task SeedDefaultTemplatesAsync(CancellationToken ct = default);
}

public sealed class FormulaTemplateSeeder : IFormulaTemplateSeeder
{
    private readonly KasaManagerDbContext _db;
    private readonly ILogger<FormulaTemplateSeeder> _logger;

    // ═══════════════════════════════════════════════════════════════
    // R21 V2: Doğru Formül Tanımları
    // ═══════════════════════════════════════════════════════════════
    // KRİTİK PRENSİP:
    // - Pool (UnifiedPool) zaten Excel'den ham değerleri getiriyor
    //   (toplam_tahsilat, toplam_reddiyat, banka_bakiye, devreden, vb.)
    // - Formüller bu ham değerleri EZMEMELİ, sadece OUTPUT hesaplamalı
    // - Genel Kasa formülleri GenelKasaR10 built-in seti ile uyumlu olmalı
    // ═══════════════════════════════════════════════════════════════

    private static readonly (string Name, string Scope, List<(string Target, string Expression)> Formulas)[] DefaultTemplates = new[]
    {
        // 1. Akşam Kasa Şablonu — P1(C)-R3 Legacy Parity
        ("AksamKasaSablonu", "Aksam", new List<(string, string)>
        {
            // Ara değerler
            ("normal_reddiyat", "Max(0.0, toplam_reddiyat - online_reddiyat)"),
            ("normal_stopaj", "Max(0.0, toplam_stopaj - online_stopaj)"),
            // Bankaya yatırılacak
            ("bankaya_yatirilacak_harc", "Max(0.0, toplam_harc + bankaya_yatirilacak_harci_degistir - kayden_harc)"),
            ("bankaya_yatirilacak_tahsilat", "Max(0.0, Max(0.0, toplam_tahsilat - normal_reddiyat) + bankaya_yatirilacak_tahsilati_degistir - vergi_kasa - kayden_tahsilat)"),
            ("bankaya_yatirilacak_toplam", "Max(0.0, bankaya_yatirilacak_tahsilat + bankaya_yatirilacak_harc + normal_stopaj - bankaya_gonderilmis_deger)"),
            // Kontrol
            ("stopaj_kontrol", "bankadan_cikan_tahsilat - online_reddiyat"),
            // Final
            ("genel_kasa", "dunden_devreden_kasa_nakit + bankadan_cekilen + vergiden_gelen + toplam_tahsilat + normal_stopaj + cesitli_nedenlerle_bankadan_cikamayan_tahsilat - normal_reddiyat - bankaya_yatirilacak_tahsilat - kayden_tahsilat"),
            ("bozuk_para_haric_kasa", "genel_kasa - bozuk_para"),
            // Muhasebe fark
            ("tahsil_red_fark", "kayden_tahsilat - bankaya_giren_tahsilat"),
            ("harc_red_fark", "kayden_harc - bankaya_giren_harc"),
            ("muhasebe_fark_tahsilat", "bankaya_giren_tahsilat - bankaya_yatirilacak_tahsilat - online_tahsilat - eft_otomatik_iade - gelen_havale - iade_kelimesi_giris - dunden_eksik_fazla_gelen_tahsilat"),
            ("muhasebe_fark_harc", "bankaya_giren_harc - bankaya_yatirilacak_harc - online_harc - dunden_eksik_fazla_gelen_harc"),
            ("kasa_toplam", "nakit_para + bozuk_para + vergiden_gelen")
        }),
        
        // 2. Sabah Kasa Şablonu — P1(C)-R3 Legacy Parity
        ("SabahKasaSablonu", "Sabah", new List<(string, string)>
        {
            // Ara değerler
            ("normal_reddiyat", "Max(0.0, toplam_reddiyat - online_reddiyat)"),
            ("normal_stopaj", "Max(0.0, toplam_stopaj - online_stopaj)"),
            // Bankaya yatırılacak
            ("bankaya_yatirilacak_harc", "Max(0.0, toplam_harc + bankaya_yatirilacak_harci_degistir - kayden_harc)"),
            ("bankaya_yatirilacak_tahsilat", "Max(0.0, Max(0.0, toplam_tahsilat - normal_reddiyat) + bankaya_yatirilacak_tahsilati_degistir - vergi_kasa - kayden_tahsilat)"),
            ("bankaya_yatirilacak_toplam", "Max(0.0, bankaya_yatirilacak_tahsilat + bankaya_yatirilacak_harc + normal_stopaj - bankaya_gonderilmis_deger)"),
            // Kontrol — SABAH sabit 0
            ("stopaj_kontrol", "0"),
            // Final
            ("genel_kasa", "dunden_devreden_kasa_nakit + bankadan_cekilen + vergiden_gelen + toplam_tahsilat + normal_stopaj + cesitli_nedenlerle_bankadan_cikamayan_tahsilat - normal_reddiyat - bankaya_yatirilacak_tahsilat - kayden_tahsilat"),
            ("bozuk_para_haric_kasa", "genel_kasa - bozuk_para"),
            // Muhasebe fark
            ("tahsil_red_fark", "kayden_tahsilat - bankaya_giren_tahsilat"),
            ("harc_red_fark", "kayden_harc - bankaya_giren_harc"),
            ("muhasebe_fark_tahsilat", "bankaya_giren_tahsilat - bankaya_yatirilacak_tahsilat - online_tahsilat - eft_otomatik_iade - gelen_havale - iade_kelimesi_giris - dunden_eksik_fazla_gelen_tahsilat"),
            ("muhasebe_fark_harc", "bankaya_giren_harc - bankaya_yatirilacak_harc - online_harc - dunden_eksik_fazla_gelen_harc"),
            ("kasa_toplam", "nakit_para + bozuk_para + vergiden_gelen"),
            // Eksik/Fazla
            ("sabah_kasa_devir", "dunden_devreden_kasa_nakit + dunden_eksik_veya_fazla_tahsilat"),
            ("gune_ait_tahsilat_farki", "kayden_tahsilat - bankaya_giren_tahsilat"),
            ("dunden_eksik_veya_fazla_tahsilat", "0"),
            ("gune_ait_eksik_veya_fazla_tahsilat", "0")
        }),
        
        // 3. Genel Kasa Şablonu — GenelKasaR10 Uyumlu
        // KRİTİK: Bu formüller sadece OUTPUT hesaplar.
        // toplam_tahsilat, toplam_reddiyat, devreden, banka_bakiye, kasa_nakit gibi
        // değerler pool'dan HAM gelir — formülle EZİLMEZ.
        ("GenelKasaSablonu", "Genel", new List<(string, string)>
        {
            // Tahsilat-Reddiyat Farkı (pool'daki ham değerlerden)
            ("tah_red_fark", "toplam_tahsilat - toplam_reddiyat"),
            // Sonraya Devredecek = Devreden + TahRedFark - GelmeyenD
            ("sonraya_devredecek", "devreden + tah_red_fark - gelmeyen_d"),
            // Beklenen Banka = SonrayaDevredecek + EksikFazla
            ("beklenen_banka", "sonraya_devredecek + eksik_fazla"),
            // Mutabakat Farkı = BankaBakiye - BeklenenBanka
            ("mutabakat_farki", "banka_bakiye - beklenen_banka"),
            // Genel Kasa = Devreden + EksikFazla + TahRedFark - BankaBakiye - KasaNakit - GelmeyenD
            ("genel_kasa", "devreden + eksik_fazla + tah_red_fark - banka_bakiye - kasa_nakit - gelmeyen_d")
        }),
        
        // 4. Ortak Kasa Şablonu (paylaşılan formüller)
        ("OrtakKasaSablonu", "Ortak", new List<(string, string)>
        {
            ("net_tahsilat", "banka_tahsilat - bankadan_iade"),
            ("net_harc", "banka_harc - harc_iade"),
            ("kasa_farki", "beklenen_kasa - gercek_kasa")
        })
    };

    /// <summary>
    /// V2: Genel Kasa formüllerinin doğru versiyonunu tanımlar.
    /// Mevcut DB'deki yanlış formülleri otomatik düzeltmek için kullanılır.
    /// </summary>
    private static readonly List<(string Target, string Expression)> CorrectGenelKasaFormulas = new()
    {
        ("tah_red_fark", "toplam_tahsilat - toplam_reddiyat"),
        ("sonraya_devredecek", "devreden + tah_red_fark - gelmeyen_d"),
        ("beklenen_banka", "sonraya_devredecek + eksik_fazla"),
        ("mutabakat_farki", "banka_bakiye - beklenen_banka"),
        ("genel_kasa", "devreden + eksik_fazla + tah_red_fark - banka_bakiye - kasa_nakit - gelmeyen_d")
    };

    public FormulaTemplateSeeder(KasaManagerDbContext db, ILogger<FormulaTemplateSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SeedDefaultTemplatesAsync(CancellationToken ct = default)
    {
        var anyExists = await _db.FormulaSets.AnyAsync(ct);
        if (anyExists)
        {
            _logger.LogDebug("R21: DB'de zaten FormulaSet var, seed atlanıyor.");
            
            // V2: Mevcut DB'deki Genel Kasa formüllerini kontrol et ve gerekirse düzelt
            await MigrateGenelKasaFormulasIfNeededAsync(ct);
            return;
        }

        _logger.LogInformation("R21: İlk çalıştırma — varsayılan şablonlar oluşturuluyor...");

        foreach (var (name, scope, formulas) in DefaultTemplates)
        {
            var template = new PersistedFormulaSet
            {
                Id = Guid.NewGuid(),
                Name = name,
                ScopeType = scope,
                IsActive = true,
                SelectedInputsJson = "[]",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Lines = formulas.Select((f, idx) => new PersistedFormulaLine
                {
                    Id = Guid.NewGuid(),
                    TargetKey = f.Target,
                    Mode = "Formula",
                    Expression = f.Expression,
                    SortOrder = idx
                }).ToList()
            };

            _db.FormulaSets.Add(template);
            _logger.LogInformation("R21: Yeni şablon oluşturuldu: {Name} ({Scope}) - {FormulaCount} formül", 
                name, scope, formulas.Count);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// V2 Migration: Genel Kasa formüllerinin R10 uyumluluğunu garanti eder.
    /// 
    /// KÖK SORUN: Erken versiyonlarda Genel Kasa, pool değerlerini ezen formüllerle
    /// seed edildi (ör. toplam_tahsilat formül target'ı olarak). Bunlar yalnızca 
    /// pool input'u olmalı, formül output'u olmamalı.
    /// 
    /// STRATEJİ (Cross-Machine Bulletproof):
    /// 1. AsNoTracking ile okuma — change tracker'a HİÇ entity yüklenmez
    /// 2. İçerik tabanlı karşılaştırma — DB'deki formüller canonical set ile birebir eşleşmeli
    /// 3. Explicit transaction — atomik: ya tümü başarılı ya hiçbiri (yarım kalma yok)
    /// 4. ExecuteDeleteAsync — server-side bulk silme, concurrency token gerekmez
    /// 5. ExecuteUpdateAsync — server-side timestamp güncelleme, tracker bypass
    /// 
    /// Bu yaklaşım şunlara BAĞIŞIKLIDIR:
    /// - DbUpdateConcurrencyException (tracked entity yok)
    /// - Farklı makinelerden gelen DB state farkları
    /// - Yarım kalmış önceki migration girişimleri (transaction rollback)
    /// </summary>
    private async Task MigrateGenelKasaFormulasIfNeededAsync(CancellationToken ct)
    {
        try
        {
            // ═══ ADIM 1: Mevcut durumu AsNoTracking ile oku ═══
            // AsNoTracking: Change tracker'a HİÇ entity eklenmez.
            // Bu, farklı makineler arasında DB taşındığında oluşan
            // concurrency token uyumsuzluğunu KÖK'ten önler.
            var genelSet = await _db.FormulaSets
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ScopeType == "Genel" && s.IsActive, ct);

            if (genelSet == null)
            {
                _logger.LogDebug("V2 Migration: Genel scope'ta aktif formül seti yok, atlanıyor.");
                return;
            }

            var currentLines = await _db.FormulaLines
                .AsNoTracking()
                .Where(l => l.SetId == genelSet.Id)
                .Select(l => new { l.TargetKey, l.Expression })
                .ToListAsync(ct);

            // ═══ ADIM 2: İçerik tabanlı karşılaştırma ═══
            // Sadece "toplam_tahsilat var mı?" yerine tüm formül setini
            // canonical tanımla birebir karşılaştır. Daha güvenilir.
            var isCorrect = currentLines.Count == CorrectGenelKasaFormulas.Count
                && CorrectGenelKasaFormulas.All(expected =>
                    currentLines.Any(c =>
                        string.Equals(c.TargetKey, expected.Target, StringComparison.Ordinal) &&
                        string.Equals(c.Expression, expected.Expression, StringComparison.Ordinal)));

            if (isCorrect)
            {
                _logger.LogDebug("V2 Migration: Genel Kasa formülleri zaten R10 uyumlu ({Count} formül).",
                    currentLines.Count);
                return;
            }

            _logger.LogWarning(
                "V2 Migration: Genel Kasa formülleri uyumsuz " +
                "(DB: {CurrentCount} formül, Beklenen: {ExpectedCount} formül). R10 uyumlu formüllerle değiştiriliyor...",
                currentLines.Count, CorrectGenelKasaFormulas.Count);

            // ═══ ADIM 3: Atomik düzeltme (explicit transaction) ═══
            // Transaction içinde: ya tüm işlemler başarılı olur, ya hiçbiri.
            // Yarım kalan migration imkansız — DB her zaman tutarlı kalır.
            var setId = genelSet.Id;
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);

            // 3a. Eski satırları server-side bulk silme
            // ExecuteDeleteAsync: Doğrudan SQL DELETE çalıştırır.
            // Change tracker'ı TAMAMEN bypass eder — concurrency token sorunu OLAMAZ.
            var deletedCount = await _db.FormulaLines
                .Where(fl => fl.SetId == setId)
                .ExecuteDeleteAsync(ct);

            // 3b. Doğru R10 uyumlu formülleri ekle (yeni entity'ler — concurrency sorunu yok)
            var newLines = CorrectGenelKasaFormulas.Select((f, idx) => new PersistedFormulaLine
            {
                Id = Guid.NewGuid(),
                SetId = setId,
                TargetKey = f.Target,
                Mode = "Formula",
                Expression = f.Expression,
                SortOrder = idx
            }).ToList();

            _db.FormulaLines.AddRange(newLines);
            await _db.SaveChangesAsync(ct);

            // 3c. Parent set timestamp güncelle (server-side, tracker bypass)
            await _db.FormulaSets
                .Where(fs => fs.Id == setId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(p => p.UpdatedAtUtc, DateTime.UtcNow), ct);

            // 3d. Tüm işlemler başarılı — commit
            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "V2 Migration: Genel Kasa '{Name}' formülleri başarıyla güncellendi. " +
                "{DeletedCount} eski formül kaldırıldı → {NewCount} R10 uyumlu formül eklendi.",
                genelSet.Name, deletedCount, newLines.Count);
        }
        catch (Exception ex)
        {
            // Transaction otomatik rollback olur (await using dispose).
            // DB orijinal durumunda kalır — veri kaybı yok.
            _logger.LogError(ex,
                "V2 Migration: Genel Kasa formül düzeltme hatası. " +
                "Transaction rollback edildi — DB orijinal formülleriyle çalışmaya devam ediyor.");
        }
    }
}
