#nullable enable
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using Microsoft.Extensions.Configuration;

namespace KasaManager.Infrastructure.Export;

/// <summary>
/// JSON dosya tabanlı şablon CRUD servisi.
/// Şablonlar WebRoot/Data/Templates/ altında saklanır.
/// Hafif ve DB bağımlılığı yoktur.
/// </summary>
public sealed class DocumentTemplateService : IDocumentTemplateService
{
    private readonly string _folder;
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public DocumentTemplateService(IConfiguration cfg)
    {
        var webRoot = cfg["WebRootPath"]
                      ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        _folder = Path.Combine(webRoot, "Data", "Templates");
        if (!Directory.Exists(_folder))
            Directory.CreateDirectory(_folder);

        // İlk çalıştırmada varsayılan şablonları oluştur
        SeedDefaultsIfEmpty();
    }

    /// <summary>
    /// Klasör boşsa 3 varsayılan banka yazısı şablonunu otomatik oluşturur.
    /// </summary>
    private void SeedDefaultsIfEmpty()
    {
        if (Directory.GetFiles(_folder, "*.json").Length > 0)
            return;

        var defaults = new[]
        {
            new DocumentTemplate
            {
                Name = "Para Çekme Yazısı",
                Category = "ParaCekme",
                HeaderText = "T.C.\nANKARA BÖLGE İDARE MAHKEMESİ BAŞKANLIĞI\nVEZNE ÖN BÜRO",
                SayiKonuText = "Sayı  : {{Tarih}}/.....Muh\nKonu : Para Çekme Yazısı",
                MuhatapText = "TÜRKİYE VAKIFLAR BANKASI\nBÖLGE İDARE MAHKEMESİ ŞUBESİNE\nANKARA",
                BodyTemplate = "Ankara Bölge İdare, İdare ve Vergi Mahkemeleri veznesinde {{Tarih}} tarihinde toplam tahsilat miktarının toplam ödeme miktarından az olması nedeniyle, Vergi Mahkemelerinde tahsil edilen harç miktarının gün sonunda maliye hesaplarına yatırılması gerektiğinden kasada bulunan nakit miktarının da bunu karşılamaması nedeniyle {{IbanHarc}} IBAN numaralı emanet hesabından {{BankadanCekilen}} çekilerek maliye harç hesaplarına aktarılmasını sağlamak amacıyla vezne katibi {{Veznedar}}'ya ödenmesi hususunda gereğini rica ederim.",
                ImzaBlokuText = "{{Hazirlayan}}\nVezne Ön Büro\nVezne Yazı İşleri Müdürü",
                FooterText = "Bu belge elektronik ortamda üretilmiştir.",
                IsActive = true
            },
            new DocumentTemplate
            {
                Name = "Virman Yazısı",
                Category = "Virman",
                HeaderText = "T.C.\nANKARA BÖLGE İDARE MAHKEMESİ BAŞKANLIĞI\nVEZNE ÖN BÜRO",
                SayiKonuText = "Sayı  : {{Tarih}}/.....Muh\nKonu : Virman Yazısı",
                MuhatapText = "TÜRKİYE VAKIFLAR BANKASI\nBÖLGE İDARE MAHKEMESİ ŞUBESİNE\nANKARA",
                BodyTemplate = "Müdürlüğümüz veznesinde {{Tarih}} tarihinde tahsil edilen stopaj tutarının {{IbanStopaj}} IBAN numaralı hesaba virman yapılması gerekmektedir. Toplam stopaj tutarı {{ToplamStopajVirman}} olup, ilgili hesaba aktarılması hususunda gereğini arz ederim.",
                ImzaBlokuText = "{{Hazirlayan}}\nVezne Ön Büro\nVezne Yazı İşleri Müdürü",
                FooterText = "Bu belge elektronik ortamda üretilmiştir.",
                IsActive = true
            },
            new DocumentTemplate
            {
                Name = "Banka Yatırım Yazısı",
                Category = "OzelTalimat",
                HeaderText = "T.C.\nANKARA BÖLGE İDARE MAHKEMESİ BAŞKANLIĞI\nVEZNE ÖN BÜRO",
                SayiKonuText = "Sayı  : {{Tarih}}/.....Muh\nKonu : Banka Yatırım Yazısı",
                MuhatapText = "TÜRKİYE VAKIFLAR BANKASI\nBÖLGE İDARE MAHKEMESİ ŞUBESİNE\nANKARA",
                BodyTemplate = "Müdürlüğümüz veznesinde {{Tarih}} tarihinde tahsil edilen tutarların aşağıda belirtilen hesaplara yatırılması gerekmektedir. Stopaj tutarı {{BankayaStopaj}} ({{IbanStopaj}}), tahsilat tutarı {{BankayaTahsilat}} ({{IbanTahsilat}}), harç tutarı {{BankayaHarc}} ({{IbanHarc}}) olmak üzere toplam {{BankayaToplam}} yatırılacaktır. Gereğini arz ederim.",
                ImzaBlokuText = "{{Hazirlayan}}\nVezne Ön Büro\nVezne Yazı İşleri Müdürü",
                FooterText = "Bu belge elektronik ortamda üretilmiştir.",
                IsActive = true
            }
        };

        foreach (var tpl in defaults)
        {
            var path = GetFilePath(tpl.Id);
            var json = JsonSerializer.Serialize(tpl, _jsonOpts);
            File.WriteAllText(path, json);
        }
    }

    public Task<IReadOnlyList<DocumentTemplate>> GetAllAsync(CancellationToken ct)
    {
        var list = LoadAll();
        return Task.FromResult<IReadOnlyList<DocumentTemplate>>(list);
    }

    public Task<IReadOnlyList<DocumentTemplate>> GetByCategoryAsync(string category, CancellationToken ct)
    {
        var list = LoadAll()
            .Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult<IReadOnlyList<DocumentTemplate>>(list);
    }

    public Task<DocumentTemplate?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var path = GetFilePath(id);
        if (!File.Exists(path))
            return Task.FromResult<DocumentTemplate?>(null);

        var json = File.ReadAllText(path);
        var template = JsonSerializer.Deserialize<DocumentTemplate>(json, _jsonOpts);
        return Task.FromResult(template);
    }

    public Task<DocumentTemplate> SaveAsync(DocumentTemplate template, CancellationToken ct)
    {
        if (template.Id == Guid.Empty)
            template.Id = Guid.NewGuid();

        template.UpdatedAtUtc = DateTime.UtcNow;

        var path = GetFilePath(template.Id);
        var json = JsonSerializer.Serialize(template, _jsonOpts);
        File.WriteAllText(path, json);

        return Task.FromResult(template);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var path = GetFilePath(id);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────

    private string GetFilePath(Guid id) => Path.Combine(_folder, $"{id}.json");

    private List<DocumentTemplate> LoadAll()
    {
        if (!Directory.Exists(_folder))
            return new List<DocumentTemplate>();

        var files = Directory.GetFiles(_folder, "*.json");
        var list = new List<DocumentTemplate>(files.Length);

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var t = JsonSerializer.Deserialize<DocumentTemplate>(json, _jsonOpts);
                if (t is not null)
                    list.Add(t);
            }
            catch
            {
                // Bozuk dosya — atla
            }
        }

        return list.OrderBy(t => t.Name).ToList();
    }
}
