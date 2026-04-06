#nullable enable
namespace KasaManager.Domain.FormulaEngine;

/// <summary>
/// R17: Tüm kullanılabilir alanların merkezi kataloğu.
/// Statik olarak tanımlanır, runtime'da değişmez.
/// Yeni alan eklemek için sadece BuildCatalog() metoduna entry eklenir.
/// </summary>
public static class FieldCatalog
{
    private static readonly Lazy<IReadOnlyList<FieldCatalogEntry>> _entries = new(BuildCatalog);
    
    /// <summary>Tüm alanları getirir</summary>
    public static IReadOnlyList<FieldCatalogEntry> All => _entries.Value;
    
    /// <summary>Kategoriye göre filtrele</summary>
    public static IEnumerable<FieldCatalogEntry> GetByCategory(string category)
        => All.Where(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
    
    /// <summary>Belirli kasa türü için varsayılan alanları getir</summary>
    public static IEnumerable<FieldCatalogEntry> GetDefaultsFor(string kasaType)
        => All.Where(x => x.DefaultVisibleIn.Contains(kasaType, StringComparer.OrdinalIgnoreCase));
    
    /// <summary>Key ile tek alan getir</summary>
    public static FieldCatalogEntry? Get(string key)
        => All.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    
    /// <summary>Tüm kategorileri getir</summary>
    public static IEnumerable<string> GetCategories()
        => All.Select(x => x.Category).Distinct().OrderBy(x => x);
    
    /// <summary>Kategorilere göre gruplanmış alanları getir</summary>
    public static IEnumerable<IGrouping<string, FieldCatalogEntry>> GetGroupedByCategory()
        => All.OrderBy(x => x.SortOrder).GroupBy(x => x.Category);
    
    /// <summary>R17B: Kaynağa göre gruplanmış alanları getir (Excel, UserInput, Calculated)</summary>
    public static IEnumerable<IGrouping<FieldSource, FieldCatalogEntry>> GetGroupedBySource()
        => All.OrderBy(x => x.SortOrder).GroupBy(x => DetermineSource(x));
    
    /// <summary>
    /// R25: SlimPool için gerekli alanları getir.
    /// HAM veriler (Excel kaynaklı) HER ZAMAN dahil edilir.
    /// Ek olarak, belirtilen kasa türü için varsayılan alanlar eklenir.
    /// </summary>
    public static HashSet<string> GetRequiredKeysFor(string kasaType)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var entry in All)
        {
            // 1. HAM veriler (Excel) → HER ZAMAN dahil
            var source = DetermineSource(entry);
            if (source == FieldSource.Excel)
            {
                result.Add(entry.Key);
                continue;
            }
            
            // 2. Kasa türüne göre varsayılan alanlar
            if (!string.IsNullOrWhiteSpace(kasaType) && 
                entry.DefaultVisibleIn.Contains(kasaType, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(entry.Key);
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// R25: Tüm ham (Excel) veri alanlarının key'lerini getir.
    /// </summary>
    public static HashSet<string> GetExcelRawKeys()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var entry in All)
        {
            if (DetermineSource(entry) == FieldSource.Excel)
            {
                result.Add(entry.Key);
            }
        }
        
        return result;
    }
    
    /// <summary>R17B: Alanın kaynak türünü belirle (otomatik algılama)</summary>
    private static FieldSource DetermineSource(FieldCatalogEntry entry)
    {
        // Önce açıkça tanımlanmış Source varsa onu kullan
        if (entry.Source != FieldSource.Excel)
            return entry.Source;
        
        // IsReadOnly = true → Hesaplanan alan
        if (entry.IsReadOnly)
            return FieldSource.Calculated;
        
        // Manuel giriş alanları (Hashset lookup)
        if (_userInputKeys.Contains(entry.Key))
            return FieldSource.UserInput;
        
        // Varsayılan: Excel'den gelen veri
        return FieldSource.Excel;
    }
    
    // Performance optimization: Static HashSet for O(1) lookup
    private static readonly HashSet<string> _userInputKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        // Kasa manuel girişleri
        "bozuk_para", "nakit_para", "kasada_kalacak_hedef", "dunden_devreden_kasa",
        "onceki_gun_devir_kasa", "kasa_nakit",
        
        // Banka manuel girişleri  
        "bankadan_cekilen", "dunden_devreden_banka_tahsilat", "dunden_devreden_banka_harc",
        
        // Değiştir alanları (ince ayar)
        "bankaya_yatirilacak_tahsilati_degistir", "bankaya_yatirilacak_harci_degistir",
        "bankaya_gonderilmis_deger", "cn_bankadan_cikamayan_tahsilat", "cn_bankadan_cikamayan_harc",
        
        // Eksik/Fazla manuel
        "dunden_eksik_fazla_tahsilat", "gune_ait_eksik_fazla_tahsilat", 
        "dunden_eksik_fazla_harc", "gune_ait_eksik_fazla_harc",
        "dunden_eksik_fazla_gelen_tahsilat", "dunden_eksik_fazla_gelen_harc",
        "dune_ait_gelen_eksik_fazla_tahsilat", "dune_ait_gelen_eksik_fazla_harc",
        "onceki_gune_ait_gelen_masraf", "eft_takilmalari",
        
        // Vergi manuel
        "vergi_kasa", "vergi_gelen_kasa", "vergiden_gelen",
        
        // Ortak kasa
        "ortak_kasa_toplam", "ortak_kasa_pay",
        
        // Genel kasa hesap manuel
        "devreden", "gelmeyen_d", "eksik_fazla", "genel_kasa_arti_eksi",

        // Ayarlar (DB) üzerinden gelen seed/override değerler
        "genel_kasa_devreden_seed", "kayden_tahsilat_ayar", "dunden_devreden_kasa_nakit",
        
        // Banka alanları
        "bankaya_giren_tahsilat", "banka_cikan_tahsilat", "banka_cekilen_tahsilat",
        "bankaya_giren_harc", "banka_cikan_harc", "banka_cekilen_harc",
        "gelmeyen_tahsilat", "gelmeyen_harc", "gelen_eft_iade",
        
        // Meta manuel
        "kasayi_yapan", "aciklama",
        
        // Kayden (elle girilen)
        "kayden_tahsilat", "kayden_harc",
        
        // PTT alanları
        "gelmeyen_post"
    };
    
    private static List<FieldCatalogEntry> BuildCatalog()
    {
        return new List<FieldCatalogEntry>
        {
            // ══════════════════════════════════════════════════════
            // AYARLAR (Seed / Override)
            // ══════════════════════════════════════════════════════
            new()
            {
                Key = "genel_kasa_devreden_seed",
                DisplayName = "Genel Kasa Devreden (Seed)",
                Category = "Ayarlar",
                SortOrder = 1,
                DefaultVisibleIn = ["Genel"],
                Description = "DB'de önceki Genel Kasa snapshot yoksa başlangıç devreden değeridir (Ayarlar).",
                Notes = "R16/R20 parity için kritik"
            },
            new()
            {
                Key = "kayden_tahsilat_ayar",
                DisplayName = "Kayden Tahsilat (Ayar)",
                Category = "Ayarlar",
                SortOrder = 2,
                DefaultVisibleIn = ["Aksam", "Sabah", "Genel"],
                Description = "Kayden Tahsilat için Ayarlardan gelen override değeridir.",
                Notes = "Boş ise MasrafveReddiyat.xlsx içinden hesaplanan değer kullanılır"
            },
            new()
            {
                Key = "dunden_devreden_kasa_nakit",
                DisplayName = "Dünden Devreden Kasa Nakit",
                Category = "Ayarlar",
                SortOrder = 3,
                DefaultVisibleIn = ["Aksam", "Sabah", "Genel"],
                Description = "Dünden devreden kasa nakit (Ayarlar/override).",
                Notes = "Eski sistem parity"
            },

            // ══════════════════════════════════════════════════════
            // TAHSİLAT
            // ══════════════════════════════════════════════════════
            new() { 
                Key = "normal_tahsilat", 
                DisplayName = "Normal Tahsilat", 
                Category = "Tahsilat", 
                SortOrder = 10,
                DefaultVisibleIn = ["Aksam", "Sabah", "Genel"], 
                Icon = "bi-cash-coin", 
                ColorClass = "text-success",
                Description = "Fiziki olarak vezneye gelen tahsilatlar"
            },
            new() { 
                Key = "online_tahsilat", 
                DisplayName = "Online Tahsilat", 
                Category = "Tahsilat", 
                SortOrder = 11,
                DefaultVisibleIn = ["Aksam", "Sabah", "Genel"],
                Icon = "bi-globe",
                Description = "Portal üzerinden yapılan online tahsilatlar"
            },
            new() { 
                Key = "post_tahsilat", 
                DisplayName = "PTT Tahsilat", 
                Category = "Tahsilat", 
                SortOrder = 12,
                DefaultVisibleIn = [], 
                Description = "PTT üzerinden gelen tahsilatlar",
                Notes = "Post işlemleri aktif edildiğinde kullanılır"
            },
            new() { 
                Key = "gelmeyen_post", 
                DisplayName = "Gelmeyen PTT", 
                Category = "Tahsilat", 
                SortOrder = 13,
                DefaultVisibleIn = [], 
                Description = "Beklenen ama henüz gelmemiş PTT tahsilatları"
            },
            new() { 
                Key = "kayden_tahsilat", 
                DisplayName = "Kayden Tahsilat", 
                Category = "Tahsilat", 
                SortOrder = 14,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                Description = "Kayıt üzerinden yapılan tahsilatlar"
            },
            new() { 
                Key = "toplam_tahsilat", 
                DisplayName = "Toplam Tahsilat", 
                Category = "Tahsilat", 
                SortOrder = 19,
                DefaultVisibleIn = ["Aksam", "Sabah", "Genel"], 
                IsReadOnly = true,
                Icon = "bi-calculator",
                Description = "Normal + Online + PTT tahsilatların toplamı"
            },
            
            // ══════════════════════════════════════════════════════
            // HARÇ
            // ══════════════════════════════════════════════════════
            new() { 
                Key = "normal_harc", 
                DisplayName = "Normal Harç", 
                Category = "Harç", 
                SortOrder = 20,
                DefaultVisibleIn = ["Aksam", "Sabah", "Genel"],
                Icon = "bi-receipt",
                Description = "Fiziki olarak alınan harçlar"
            },
            new() { 
                Key = "online_harc", 
                DisplayName = "Online Harç", 
                Category = "Harç", 
                SortOrder = 21,
                DefaultVisibleIn = ["Aksam", "Sabah", "Genel"],
                Description = "Portal üzerinden alınan harçlar"
            },
            new() { 
                Key = "post_harc", 
                DisplayName = "PTT Harç", 
                Category = "Harç", 
                SortOrder = 22,
                DefaultVisibleIn = [],
                Description = "PTT üzerinden alınan harçlar"
            },
            new() { 
                Key = "kayden_harc", 
                DisplayName = "Kayden Harç", 
                Category = "Harç", 
                SortOrder = 23,
                DefaultVisibleIn = ["Aksam", "Sabah"]
            },
            new() { 
                Key = "toplam_harc", 
                DisplayName = "Toplam Harç", 
                Category = "Harç", 
                SortOrder = 29,
                DefaultVisibleIn = ["Aksam", "Sabah", "Genel"], 
                IsReadOnly = true,
                Icon = "bi-calculator"
            },
            
            // ══════════════════════════════════════════════════════
            // REDDİYAT
            // ══════════════════════════════════════════════════════
            new() { 
                Key = "normal_reddiyat", 
                DisplayName = "Normal Reddiyat", 
                Category = "Reddiyat", 
                SortOrder = 30,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                Icon = "bi-arrow-return-left",
                ColorClass = "text-danger",
                Description = "Fiziki olarak yapılan geri ödemeler"
            },
            new() { 
                Key = "online_reddiyat", 
                DisplayName = "Online Reddiyat", 
                Category = "Reddiyat", 
                SortOrder = 31,
                DefaultVisibleIn = ["Aksam", "Sabah", "Genel"],
                Description = "Banka üzerinden yapılan geri ödemeler (EFT/Havale)"
            },
            new() { 
                Key = "toplam_reddiyat", 
                DisplayName = "Toplam Reddiyat", 
                Category = "Reddiyat", 
                SortOrder = 39,
                DefaultVisibleIn = ["Aksam", "Sabah", "Genel"], 
                IsReadOnly = true,
                Icon = "bi-calculator"
            },
            
            // ══════════════════════════════════════════════════════
            // STOPAJ
            // ══════════════════════════════════════════════════════
            new() { 
                Key = "normal_stopaj", 
                DisplayName = "Normal Stopaj", 
                Category = "Stopaj", 
                SortOrder = 40,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                Icon = "bi-percent",
                Description = "Fiziki reddiyattan kesilen stopaj"
            },
            new() { 
                Key = "online_stopaj", 
                DisplayName = "Online Stopaj", 
                Category = "Stopaj", 
                SortOrder = 41,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                Description = "Online reddiyattan kesilen stopaj (Gelir Vergisi + Damga Vergisi)"
            },
            new() { 
                Key = "toplam_stopaj", 
                DisplayName = "Toplam Stopaj", 
                Category = "Stopaj", 
                SortOrder = 42,
                DefaultVisibleIn = ["Aksam", "Sabah"], 
                IsReadOnly = true,
                Icon = "bi-calculator"
            },
            new() { 
                Key = "stopaj_kontrol", 
                DisplayName = "Stopaj Kontrol", 
                Category = "Stopaj", 
                SortOrder = 43,
                DefaultVisibleIn = [],
                Description = "Stopaj mutabakat kontrolü"
            },
            new() { 
                Key = "stopaj_virman_durumu", 
                DisplayName = "Stopaj Virman Durumu", 
                Category = "Stopaj", 
                SortOrder = 44,
                DefaultVisibleIn = [],
                DataType = "string",
                Description = "Stopajın virmanlanıp virmanlanmadığı"
            },
            
            // ══════════════════════════════════════════════════════
            // EKSİK/FAZLA
            // ══════════════════════════════════════════════════════
            new() { 
                Key = "dunden_eksik_fazla_tahsilat", 
                DisplayName = "Dünden Eksik/Fazla Tahsilat", 
                Category = "Eksik/Fazla", 
                SortOrder = 50,
                DefaultVisibleIn = ["Sabah"], 
                Description = "Bir önceki günden kalan eksiklik veya fazlalık", 
                Icon = "bi-arrow-left-right", 
                ColorClass = "text-warning"
            },
            new() { 
                Key = "gune_ait_eksik_fazla_tahsilat", 
                DisplayName = "Güne Ait Eksik/Fazla Tahsilat", 
                Category = "Eksik/Fazla", 
                SortOrder = 51,
                DefaultVisibleIn = ["Sabah"], 
                Description = "Bugüne ait eksiklik veya fazlalık"
            },
            new() { 
                Key = "dunden_eksik_fazla_gelen_tahsilat", 
                DisplayName = "Önceki Günden Gelen Eksik/Fazla", 
                Category = "Eksik/Fazla", 
                SortOrder = 52,
                DefaultVisibleIn = ["Sabah"], 
                Description = "Dünkü eksik, bugün geldi = bugünün fazlası"
            },
            new() { 
                Key = "dunden_eksik_fazla_harc", 
                DisplayName = "Dünden Eksik/Fazla Harç", 
                Category = "Eksik/Fazla", 
                SortOrder = 53,
                DefaultVisibleIn = ["Sabah"]
            },
            new() { 
                Key = "gune_ait_eksik_fazla_harc", 
                DisplayName = "Güne Ait Eksik/Fazla Harç", 
                Category = "Eksik/Fazla", 
                SortOrder = 54,
                DefaultVisibleIn = ["Sabah"]
            },
            new() { 
                Key = "dunden_eksik_fazla_gelen_harc", 
                DisplayName = "Önceki Günden Gelen Eksik/Fazla Harç", 
                Category = "Eksik/Fazla", 
                SortOrder = 55,
                DefaultVisibleIn = ["Sabah"]
            },
            new() { 
                Key = "onceki_gune_ait_gelen_masraf", 
                DisplayName = "Önceki Güne Ait Gelen Masraf", 
                Category = "Eksik/Fazla", 
                SortOrder = 56,
                DefaultVisibleIn = [], 
                Description = "EFT takılmaları - sonraki gün geldi"
            },
            new() { 
                Key = "eft_takilmalari", 
                DisplayName = "EFT Takılmaları", 
                Category = "Eksik/Fazla", 
                SortOrder = 57,
                DefaultVisibleIn = [], 
                Description = "Aynı gün bankaya yansımayan EFT'ler"
            },
            new() { 
                Key = "dune_ait_gelen_eksik_fazla_tahsilat", 
                DisplayName = "Düne Ait Gelen Eksik/Fazla Tahsilat", 
                Category = "Eksik/Fazla", 
                SortOrder = 58,
                DefaultVisibleIn = ["Sabah"]
            },
            new() { 
                Key = "dune_ait_gelen_eksik_fazla_harc", 
                DisplayName = "Düne Ait Gelen Eksik/Fazla Harç", 
                Category = "Eksik/Fazla", 
                SortOrder = 59,
                DefaultVisibleIn = ["Sabah"]
            },
            
            // ══════════════════════════════════════════════════════
            // BANKA
            // ══════════════════════════════════════════════════════
            new() { 
                Key = "dunden_devreden_banka_tahsilat", 
                DisplayName = "Dünden Devreden Banka (Tahsilat)", 
                Category = "Banka", 
                SortOrder = 60,
                DefaultVisibleIn = ["Sabah"],
                Icon = "bi-bank",
                Description = "Önceki günden devreden banka tahsilat bakiyesi"
            },
            new() { 
                Key = "yarina_deverecek_banka_tahsilat", 
                DisplayName = "Yarına Devredecek Banka (Tahsilat)", 
                Category = "Banka", 
                SortOrder = 61,
                DefaultVisibleIn = ["Sabah"], 
                IsReadOnly = true,
                Description = "Sonraki güne devredecek banka tahsilat bakiyesi"
            },
            new() { 
                Key = "bankaya_giren_tahsilat", 
                DisplayName = "Bankaya Giren Tahsilat", 
                Category = "Banka", 
                SortOrder = 62,
                DefaultVisibleIn = ["Sabah"]
            },
            new() { 
                Key = "bankadan_cikan_tahsilat", 
                DisplayName = "Bankadan Çıkan Tahsilat", 
                Category = "Banka", 
                SortOrder = 63,
                DefaultVisibleIn = ["Sabah"]
            },
            new() { 
                Key = "banka_cekilen_tahsilat", 
                DisplayName = "Bankadan Çekilen Tahsilat", 
                Category = "Banka", 
                SortOrder = 64,
                DefaultVisibleIn = ["Sabah"]
            },
            new() { 
                Key = "dunden_devreden_banka_harc", 
                DisplayName = "Dünden Devreden Banka (Harç)", 
                Category = "Banka", 
                SortOrder = 65,
                DefaultVisibleIn = ["Sabah"]
            },
            new() { 
                Key = "yarina_deverecek_banka_harc", 
                DisplayName = "Yarına Devredecek Banka (Harç)", 
                Category = "Banka", 
                SortOrder = 66,
                DefaultVisibleIn = ["Sabah"], 
                IsReadOnly = true
            },
            new() { 
                Key = "bankaya_giren_harc", 
                DisplayName = "Bankaya Giren Harç", 
                Category = "Banka", 
                SortOrder = 67,
                DefaultVisibleIn = ["Sabah"]
            },
            new() { 
                Key = "bankadan_cikan_harc", 
                DisplayName = "Bankadan Çıkan Harç", 
                Category = "Banka", 
                SortOrder = 68,
                DefaultVisibleIn = ["Sabah"]
            },
            new() { 
                Key = "banka_cekilen_harc", 
                DisplayName = "Bankadan Çekilen Harç", 
                Category = "Banka", 
                SortOrder = 69,
                DefaultVisibleIn = ["Sabah"]
            },
            new() { 
                Key = "banka_bakiye", 
                DisplayName = "Banka Bakiye", 
                Category = "Banka", 
                SortOrder = 70,
                DefaultVisibleIn = ["Genel"], 
                IsReadOnly = true,
                Icon = "bi-bank2",
                Description = "Güncel banka bakiyesi"
            },
            new() { 
                Key = "bankadan_cekilen", 
                DisplayName = "Bankadan Çekilen", 
                Category = "Banka", 
                SortOrder = 71,
                DefaultVisibleIn = ["Aksam"],
                Description = "Kasaya çekilen nakit"
            },
            new() { 
                Key = "gelmeyen_tahsilat", 
                DisplayName = "Gelmeyen Tahsilat", 
                Category = "Banka", 
                SortOrder = 72,
                DefaultVisibleIn = ["Sabah"]
            },
            new() { 
                Key = "gelmeyen_harc", 
                DisplayName = "Gelmeyen Harç", 
                Category = "Banka", 
                SortOrder = 73,
                DefaultVisibleIn = ["Sabah"]
            },
            new() { 
                Key = "eft_otomatik_iade", 
                DisplayName = "EFT Otomatik İade", 
                Category = "Banka", 
                SortOrder = 74,
                DefaultVisibleIn = ["Sabah"],
                Source = FieldSource.Excel,
                Description = "Gelen EFT Otomatik Yatan (BankaTahsilat.xlsx)"
            },
            new() { 
                Key = "gelen_havale", 
                DisplayName = "Gelen Havale", 
                Category = "Banka", 
                SortOrder = 75,
                DefaultVisibleIn = ["Sabah"],
                Source = FieldSource.Excel,
                Description = "Gelen Havale (BankaTahsilat.xlsx)"
            },
            new() { 
                Key = "iade_kelimesi_giris", 
                DisplayName = "İade Kelimesi Giriş", 
                Category = "Banka", 
                SortOrder = 76,
                DefaultVisibleIn = ["Sabah"],
                Source = FieldSource.Excel,
                Description = "Açıklama/İşlem adında 'iade' geçen girişler (BankaTahsilat.xlsx)"
            },
            
            // ══════════════════════════════════════════════════════
            // KASA
            // ══════════════════════════════════════════════════════
            new() { 
                Key = "dunden_devreden_kasa", 
                DisplayName = "Dünden Devreden Kasa", 
                Category = "Kasa", 
                SortOrder = 80,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                Icon = "bi-safe",
                Description = "Önceki günden devreden kasa bakiyesi"
            },
            new() { 
                Key = "genel_kasa", 
                DisplayName = "Genel Kasa", 
                Category = "Kasa", 
                SortOrder = 81,
                DefaultVisibleIn = ["Aksam", "Sabah", "Genel"], 
                IsReadOnly = true,
                Icon = "bi-cash-stack",
                ColorClass = "text-primary"
            },
            new() { 
                Key = "bozuk_para", 
                DisplayName = "Bozuk Para", 
                Category = "Kasa", 
                SortOrder = 82,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                Description = "Kasadaki bozuk para miktarı"
            },
            new() { 
                Key = "bozuk_para_haric_kasa", 
                DisplayName = "Bozuk Para Hariç Kasa", 
                Category = "Kasa", 
                SortOrder = 83,
                DefaultVisibleIn = ["Aksam", "Sabah"], 
                IsReadOnly = true
            },
            new() { 
                Key = "nakit_para", 
                DisplayName = "Nakit Para", 
                Category = "Kasa", 
                SortOrder = 84,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                Description = "Kasadaki nakit para miktarı"
            },
            new() { 
                Key = "kasa_nakit", 
                DisplayName = "Kasa Nakit", 
                Category = "Kasa", 
                SortOrder = 85,
                DefaultVisibleIn = ["Genel"],
                Description = "Genel Kasa için nakit değeri"
            },
            new() { 
                Key = "onceki_gun_devir_kasa", 
                DisplayName = "Önceki Gün Devir Kasa", 
                Category = "Kasa", 
                SortOrder = 86,
                DefaultVisibleIn = ["Sabah"]
            },
            new() { 
                Key = "kasada_kalacak_hedef", 
                DisplayName = "Kasada Kalacak Hedef", 
                Category = "Kasa", 
                SortOrder = 87,
                DefaultVisibleIn = ["Aksam"],
                Description = "Bozuk dahil hedef bakiye"
            },
            new() { 
                Key = "uyap_bakiye", 
                DisplayName = "UYAP Bakiye", 
                Category = "Kasa", 
                SortOrder = 88,
                DefaultVisibleIn = ["Sabah"],
                IsReadOnly = true
            },
            
            // ══════════════════════════════════════════════════════
            // BANKA YATIRIM
            // ══════════════════════════════════════════════════════
            new() { 
                Key = "bankaya_yatirilacak_nakit", 
                DisplayName = "Bankaya Yatırılacak Nakit", 
                Category = "Banka Yatırım", 
                SortOrder = 90,
                DefaultVisibleIn = ["Aksam", "Sabah"], 
                IsReadOnly = true,
                Icon = "bi-box-arrow-in-down"
            },
            new() { 
                Key = "bankaya_yatirilacak_tahsilat", 
                DisplayName = "Bankaya Yatırılacak Tahsilat", 
                Category = "Banka Yatırım", 
                SortOrder = 91,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                IsReadOnly = true
            },
            new() { 
                Key = "bankaya_yatirilacak_tahsilati_degistir", 
                DisplayName = "Yt.Tahs. Değiştir (+/-)", 
                Category = "Banka Yatırım", 
                SortOrder = 92,
                DefaultVisibleIn = ["Aksam"], 
                Description = "Hedefin üstüne manuel ince ayar"
            },
            new() { 
                Key = "bankaya_yatirilacak_harc", 
                DisplayName = "Bankaya Yatırılacak Harç", 
                Category = "Banka Yatırım", 
                SortOrder = 93,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                IsReadOnly = true
            },
            new() { 
                Key = "bankaya_yatirilacak_harci_degistir", 
                DisplayName = "Yt.Harç Değiştir (+/-)", 
                Category = "Banka Yatırım", 
                SortOrder = 94,
                DefaultVisibleIn = ["Aksam"]
            },
            new() { 
                Key = "bankaya_yatirilacak_stopaj", 
                DisplayName = "Bankaya Yatırılacak Stopaj", 
                Category = "Banka Yatırım", 
                SortOrder = 95,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                IsReadOnly = true
            },
            new() { 
                Key = "bankaya_gonderilmis_deger", 
                DisplayName = "Bankaya Gönderilmiş Değer", 
                Category = "Banka Yatırım", 
                SortOrder = 96,
                DefaultVisibleIn = ["Aksam"], 
                Description = "B.Top.Götürülecek hesabından düşülür"
            },
            new() { 
                Key = "b_toplam_yatan", 
                DisplayName = "B.Toplam Yatan", 
                Category = "Banka Yatırım", 
                SortOrder = 97,
                DefaultVisibleIn = ["Sabah"],
                IsReadOnly = true
            },
            new() { 
                Key = "cn_bankadan_cikamayan_tahsilat", 
                DisplayName = "Ç.N. Bankadan Çıkmayan Tahsilat", 
                Category = "Banka Yatırım", 
                SortOrder = 98,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                Description = "Çeşitli nedenlerle bankadan çıkamayan"
            },
            new() { 
                Key = "cn_bankadan_cikamayan_harc", 
                DisplayName = "Ç.N. Bankadan Çıkmayan Harç", 
                Category = "Banka Yatırım", 
                SortOrder = 99,
                DefaultVisibleIn = []
            },
            new() { 
                Key = "gelen_eft_iade", 
                DisplayName = "Gelen EFT İade", 
                Category = "Banka Yatırım", 
                SortOrder = 100,
                DefaultVisibleIn = ["Sabah"]
            },
            
            // ══════════════════════════════════════════════════════
            // VERGİ
            // ══════════════════════════════════════════════════════
            new() { 
                Key = "vergi_kasa", 
                DisplayName = "Vergi Kasa", 
                Category = "Vergi", 
                SortOrder = 110,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                Icon = "bi-building"
            },
            new() { 
                Key = "vergi_gelen_kasa", 
                DisplayName = "Vergi Gelen Kasa", 
                Category = "Vergi", 
                SortOrder = 111,
                DefaultVisibleIn = ["Aksam", "Sabah"]
            },
            new() { 
                Key = "vergiden_gelen", 
                DisplayName = "Vergiden Gelen", 
                Category = "Vergi", 
                SortOrder = 112,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                Description = "Manuel giriş - Excel'den gelmez"
            },
            new() { 
                Key = "gelir_vergisi", 
                DisplayName = "Gelir Vergisi", 
                Category = "Vergi", 
                SortOrder = 113,
                DefaultVisibleIn = [],
                Description = "Reddiyattan kesilen gelir vergisi"
            },
            new() { 
                Key = "damga_vergisi", 
                DisplayName = "Damga Vergisi", 
                Category = "Vergi", 
                SortOrder = 114,
                DefaultVisibleIn = [],
                Description = "Reddiyattan kesilen damga vergisi"
            },
            
            // ══════════════════════════════════════════════════════
            // ORTAK KASA (Yeni)
            // ══════════════════════════════════════════════════════
            new() { 
                Key = "ortak_kasa_toplam", 
                DisplayName = "Ortak Kasa Toplam", 
                Category = "Ortak Kasa", 
                SortOrder = 120,
                DefaultVisibleIn = ["Ortak"],
                Icon = "bi-people"
            },
            new() { 
                Key = "ortak_kasa_pay", 
                DisplayName = "Ortak Kasa Pay", 
                Category = "Ortak Kasa", 
                SortOrder = 121,
                DefaultVisibleIn = ["Ortak"]
            },
            new() { 
                Key = "ortak_kasa_fark", 
                DisplayName = "Ortak Kasa Fark", 
                Category = "Ortak Kasa", 
                SortOrder = 122,
                DefaultVisibleIn = ["Ortak"], 
                IsReadOnly = true
            },
            
            // ══════════════════════════════════════════════════════
            // GENEL KASA HESAP (R10)
            // ══════════════════════════════════════════════════════
            new() { 
                Key = "devreden", 
                DisplayName = "Devreden Bakiye", 
                Category = "Genel Kasa Hesap", 
                SortOrder = 130,
                DefaultVisibleIn = ["Genel"],
                Description = "Dönem başı bakiye"
            },
            new() { 
                Key = "tah_red_fark", 
                DisplayName = "Tahsilat-Reddiyat Farkı", 
                Category = "Genel Kasa Hesap", 
                SortOrder = 131,
                DefaultVisibleIn = ["Genel"], 
                IsReadOnly = true
            },
            new() { 
                Key = "gelmeyen_d", 
                DisplayName = "Gelmeyen D.", 
                Category = "Genel Kasa Hesap", 
                SortOrder = 132,
                DefaultVisibleIn = ["Genel"], 
                Description = "Beklenen ama gelmemiş tahsilat"
            },
            new() { 
                Key = "eksik_fazla", 
                DisplayName = "Eksik/Fazla", 
                Category = "Genel Kasa Hesap", 
                SortOrder = 133,
                DefaultVisibleIn = ["Genel"]
            },
            new() { 
                Key = "sonraya_devredecek", 
                DisplayName = "Sonraya Devredecek", 
                Category = "Genel Kasa Hesap", 
                SortOrder = 134,
                DefaultVisibleIn = ["Genel"], 
                IsReadOnly = true
            },
            new() { 
                Key = "beklenen_banka", 
                DisplayName = "Beklenen Banka", 
                Category = "Genel Kasa Hesap", 
                SortOrder = 135,
                DefaultVisibleIn = ["Genel"], 
                IsReadOnly = true
            },
            new() { 
                Key = "mutabakat_farki", 
                DisplayName = "Mutabakat Farkı", 
                Category = "Genel Kasa Hesap", 
                SortOrder = 136,
                DefaultVisibleIn = ["Genel"], 
                IsReadOnly = true,
                ColorClass = "text-danger",
                Description = "Banka Bakiye - Beklenen Banka"
            },
            new() { 
                Key = "genel_kasa_arti_eksi", 
                DisplayName = "Genel Kasa (+/-)", 
                Category = "Genel Kasa Hesap", 
                SortOrder = 137,
                DefaultVisibleIn = ["Sabah"]
            },
            
            // ══════════════════════════════════════════════════════
            // MASRAF
            // ══════════════════════════════════════════════════════
            new() { 
                Key = "online_masraf", 
                DisplayName = "Online Masraf", 
                Category = "Masraf", 
                SortOrder = 140,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                Description = "Portal üzerinden yapılan masraflar"
            },
            new() { 
                Key = "masraf", 
                DisplayName = "Masraf", 
                Category = "Masraf", 
                SortOrder = 141,
                DefaultVisibleIn = ["Aksam", "Sabah", "Genel"],
                Description = "Dosya üzerinden okunan Masraf"
            },
            new() { 
                Key = "masraf_reddiyat", 
                DisplayName = "Masraf Reddiyat", 
                Category = "Masraf", 
                SortOrder = 142,
                DefaultVisibleIn = ["Aksam", "Sabah", "Genel"],
                Description = "Dosya üzerinden okunan Reddiyat"
            },
            
            // ══════════════════════════════════════════════════════
            // META
            // ══════════════════════════════════════════════════════
            new() { 
                Key = "islem_tarihi", 
                DisplayName = "İşlem Tarihi", 
                Category = "Meta", 
                DataType = "date", 
                SortOrder = 200,
                DefaultVisibleIn = ["Aksam", "Sabah", "Genel"], 
                IsReadOnly = true,
                Icon = "bi-calendar"
            },
            new() { 
                Key = "kasayi_yapan", 
                DisplayName = "Kasayı Yapan", 
                Category = "Meta", 
                DataType = "string", 
                SortOrder = 201,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                Icon = "bi-person"
            },
            new() { 
                Key = "aciklama", 
                DisplayName = "Açıklama", 
                Category = "Meta", 
                DataType = "string", 
                SortOrder = 202,
                DefaultVisibleIn = ["Aksam", "Sabah"],
                Description = "Kasa için not/açıklama"
            },
        };
    }
}
