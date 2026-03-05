#nullable enable
using KasaManager.Domain.FormulaEngine.Authoring;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// R21: Varsayılan formül şablonlarını veritabanına seed eder.
/// Uygulama başlangıcında çağrılır, eksik şablonları oluşturur.
/// </summary>
public interface IFormulaTemplateSeeder
{
    Task SeedDefaultTemplatesAsync(CancellationToken ct = default);
}

public sealed class FormulaTemplateSeeder : IFormulaTemplateSeeder
{
    private readonly KasaManagerDbContext _db;
    private readonly ILogger<FormulaTemplateSeeder> _logger;

    // R21: 4 Varsayılan Şablon Tanımları
    private static readonly (string Name, string Scope, List<(string Target, string Expression)> Formulas)[] DefaultTemplates = new[]
    {
        // 1. Akşam Kasa Şablonu
        ("AksamKasaSablonu", "Aksam", new List<(string, string)>
        {
            ("genel_kasa", "banka_tahsilat + online_tahsilat + nakit_para + bozuk_para - banka_harc"),
            ("tahsil_red_fark", "kayden_tahsilat - banka_tahsilat"),
            ("harc_red_fark", "kayden_harc - banka_harc"),
            ("bankaya_yatirilacak_harç", "banka_harc + bankaya_yatirilacak_harci_degistir"),
            ("bankaya_yatirilacak_tahsilat", "banka_tahsilat + bankaya_yatirilacak_tahsilati_degistir"),
            ("kasa_toplam", "nakit_para + bozuk_para + vergiden_gelen")
        }),
        
        // 2. Sabah Kasa Şablonu
        ("SabahKasaSablonu", "Sabah", new List<(string, string)>
        {
            ("sabah_kasa_devir", "dunden_devreden_kasa_nakit + dunden_eksik_veya_fazla_tahsilat"),
            ("sabah_toplam", "sabah_kasa_devir + banka_tahsilat + online_tahsilat"),
            ("gune_ait_tahsilat_farki", "kayden_tahsilat - banka_tahsilat"),
            ("dunden_eksik_veya_fazla_tahsilat", "0"), // Placeholder
            ("gune_ait_eksik_veya_fazla_tahsilat", "0") // Placeholder
        }),
        
        // 3. Genel Kasa Şablonu - Kapsamlı formüller
        ("GenelKasaSablonu", "Genel", new List<(string, string)>
        {
            // Temel hesaplamalar
            ("kasa_nakit", "bozuk_para + nakit_para"),
            ("toplam_tahsilat", "banka_cekilen_tahsilat + online_tahsilat + vergiden_gelen + gelmeyen_d"),
            ("toplam_harc", "banka_cekilen_harc + diger_harclar + masraf + masraf_reddiyat"),
            ("genel_kasa_toplam", "genel_kasa_devreden_seed + toplam_tahsilat - toplam_harc + kasa_nakit"),
            ("genel_kasa_devir", "genel_kasa_toplam - bankaya_yatirilacak_tahsilat"),
            
            // Banka hesaplamaları
            ("banka_tahsilat", "banka_cekilen_tahsilat + online_tahsilat"),
            ("bankaya_yatirilacak_tahsilat", "banka_tahsilat + bankaya_yatirilacak_tahsilati_degistir"),
            ("banka_harc", "banka_cekilen_harc"),
            ("banka_yarina_devredecek_tahsilat", "genel_kasa_devir"),
            
            // Fark hesaplamaları
            ("tahsil_red_fark", "kayden_tahsilat - banka_tahsilat"),
            ("harc_red_fark", "kayden_harc - banka_harc"),
            ("kasa_eksik_fazla", "beklenen_kasa - gercek_kasa")
        }),
        
        // 4. Ortak Kasa Şablonu (paylaşılan formüller)
        ("OrtakKasaSablonu", "Ortak", new List<(string, string)>
        {
            ("net_tahsilat", "banka_tahsilat - bankadan_iade"),
            ("net_harc", "banka_harc - harc_iade"),
            ("kasa_farki", "beklenen_kasa - gercek_kasa")
        })
    };

    public FormulaTemplateSeeder(KasaManagerDbContext db, ILogger<FormulaTemplateSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SeedDefaultTemplatesAsync(CancellationToken ct = default)
    {
        // R21 FIX: Sadece DB'de hiç FormulaSet yoksa seed et (ilk çalıştırma).
        // Önceki mantık her şablonu Name+ScopeType ile kontrol ediyordu;
        // kullanıcı bir şablonu sildiğinde, sonraki restart'ta tekrar oluşturuyordu.
        var anyExists = await _db.FormulaSets.AnyAsync(ct);
        if (anyExists)
        {
            _logger.LogDebug("R21: DB'de zaten FormulaSet var, seed atlanıyor.");
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
}
