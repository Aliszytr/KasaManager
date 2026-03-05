using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services;

/// <summary>
/// Her rapor tipi için zorunlu kolonlar ve alias'lar.
/// Not: Alias'larda çakışma OLMAZ. (Örn: "Tutar" hem miktar hem islem_tutari olamaz)
/// </summary>
public static class ImportProfiles
{
    public static ExcelReadOptions GetOptions(ImportFileKind kind)
    {
        // ⚠️ KRİTİK: Büyük dosyalar (özellikle MasrafveReddiyat / Banka* gibi) satır limiti yüzünden
        // eksik okunursa R16 UnifiedPool değerleri R10 True Source değerleriyle tutmaz.
        // Bu yüzden bazı rapor tiplerinde default olarak limitsiz okuyoruz.
        // (İleride istenirse appsettings üzerinden "preview max rows" gibi bir ayarla tekrar kontrollü limitlenebilir.)
        var needsFullRead = kind is ImportFileKind.MasrafVeReddiyat
            or ImportFileKind.BankaTahsilat
            or ImportFileKind.BankaHarcama;

        var opt = new ExcelReadOptions
        {
            SkipEmptyRows = true,
            // Ali notu (2026-01): Preview/Import katmanlarında satır limiti yüzünden eksik okuma yaşandı.
            // Kritik dosyalarda limit YOK; diğerlerinde de "gerçek hayat" için limit 20.000'e çekildi.
            MaxRows = needsFullRead ? null : 20000
        };

        // =========================
        // ORTAK (genel) kolonlar
        // =========================
        opt.ColumnAliases["dosya_no"] = new[]
        {
            "Dosya No", "DosyaNo", "Dosya Numarası", "Dosya Numarasi"
        };

        opt.ColumnAliases["personel_adi"] = new[]
        {
            "Personel Adı", "PersonelAdi", "Personel Adi"
        };

        opt.ColumnAliases["birim_adi"] = new[]
        {
            "Birim Adı", "BirimAdi", "Birim Adi",
            "Birim Adıı"  // OnlineReddiyat.xlsx'deki typo
        };

        // Online/Masraf/Reddiyat gibi raporlarda kullanılan miktar
        // ⚠️ Banka "İşlem Tutarı/Tutar" burada OLMAMALI (çakışma yapar)
        opt.ColumnAliases["miktar"] = new[]
        {
            "Miktar",
            "Ödenecek Miktar",
            "Yatırılan Miktar",
            "Ödenebilir Miktar"
        };

        opt.ColumnAliases["tarih"] = new[]
        {
            "Tarih",
            "Reddiyat Tar.",
            "Reddiyat Tarihi"
        };

        // =========================
        // KasaÜstRapor TOPLAMLAR kolonları (Harç / Online Harç / Tahsilat vb.)
        // Not: Bu alias'lar diğer rapor tipleriyle çakışmaz (ayrı canonical isimler).
        // =========================
        opt.ColumnAliases["tahsilat"] = new[]
        {
            "Tahsilat",
            "Toplam Tahsilat",
            "Tahsilat (TL)",
            "Toplam Tahsilat (TL)"
        };

        opt.ColumnAliases["pos_tahsilat"] = new[]
        {
            "Pos Tahsilat",
            "POS Tahsilat",
            "PosTahsilat",
            "POSTahsilat"
        };

        opt.ColumnAliases["online_tahsilat"] = new[]
        {
            "Online Tahsilat",
            "OnlineTahsilat"
        };

        opt.ColumnAliases["harc"] = new[]
        {
            "Harç",
            "Harc",
            "Toplam Harç",
            "Toplam Harc",
            "Harç (TL)",
            "Toplam Harç (TL)"
        };

        opt.ColumnAliases["pos_harc"] = new[]
        {
            "Pos Harç",
            "POS Harç",
            "PosHarc",
            "POSHarc"
        };

        opt.ColumnAliases["online_harc"] = new[]
        {
            "Online Harç",
            "Online Harc",
            "OnlineHarc"
        };

        
        opt.ColumnAliases["post_tahsilat"] = new[]
        {
            "Post Tahsilat",
            "PostTahsilat",
            "Post Tahsilat (TL)",
            "PostTahsilat(TL)"
        };

        opt.ColumnAliases["post_harc"] = new[]
        {
            "Post Harç",
            "Post Harc",
            "PostHarc",
            "Post Harç (TL)",
            "Post Harc (TL)",
            "PostHarc(TL)"
        };

        opt.ColumnAliases["gelmeyen_post"] = new[]
        {
            "Gelmeyen Post",
            "GelmeyenPost",
            "Gelmeyen Post (TL)",
            "GelmeyenPost(TL)"
        };

        opt.ColumnAliases["online_masraf"] = new[]
        {
            "Online Masraf",
            "OnlineMasraf",
            "Online Masraf (TL)",
            "OnlineMasraf(TL)"
        };

opt.ColumnAliases["reddiyat"] = new[]
        {
            "Reddiyat",
            "Toplam Reddiyat",
            "Reddiyat (TL)",
            "Toplam Reddiyat (TL)"
        };

        opt.ColumnAliases["stopaj"] = new[]
        {
            "Stopaj",
            "Toplam Stopaj"
        };

        opt.ColumnAliases["gelir_vergisi"] = new[]
        {
            "Gelir Vergisi",
            "GelirVergisi",
            "Gelir Ver.",   // OnlineReddiyat.xlsx kısaltması
            "Gelir Ver"
        };

        opt.ColumnAliases["damga_vergisi"] = new[]
        {
            "Damga Vergisi",
            "DamgaVergisi",
            "Damga Ver.",   // OnlineReddiyat.xlsx kısaltması
            "Damga Ver"
        };

        opt.ColumnAliases["referans_no"] = new[]
        {
            "Referans No",
            "ReferansNo",
            "Referans Numarası",
            "Ref No",
            "Ref",
            "Referans"
        };

        opt.ColumnAliases["odenecek_kisi"] = new[]
        {
            "Ödenecek Kişi",
            "OdenecekKisi",
            "Ödenecek",
            "Alıcı",
            "Alici"
        };

        // =========================
        // BANKA kolonları (BankaHarc/BankaTahsilat)
        // =========================
        opt.ColumnAliases["islem_tarihi"] = new[]
        {
            "İşlem Tarihi",
            "Islem Tarihi",
            "İşlemTarihi",
            "IslemTarihi"
        };

        opt.ColumnAliases["islem_tutari"] = new[]
        {
            "İşlem Tutarı",
            " İşlem Tutarı",     // Başında boşluk olan versiyon (BankaTahsilat'ta var)
            "Islem Tutari",
            "İşlemTutarı",
            "IslemTutari",
            "Tutar",              // Banka dosyalarında Tutar = işlem tutarı
            "İşlem Tutarı (TL)",
            "Islem Tutari (TL)"
        };

        opt.ColumnAliases["islem_sonrasi_bakiye"] = new[]
        {
            "İşlem Sonrası Bakiye",
            "Islem Sonrasi Bakiye",
            "Bakiye",
            "Bakiye (TL)",
            "Bakiye(TL)"
        };

        opt.ColumnAliases["islem_adi"] = new[]
        {
    "İşlem Adı",
    "Islem Adi"
};

        opt.ColumnAliases["aciklama"] = new[]
        {
    "Açıklama",
    "Aciklama"
};


        // =========================
        // Rapor tipine göre zorunlular
        // =========================
        switch (kind)
        {
            case ImportFileKind.BankaHarcama:
            case ImportFileKind.BankaTahsilat:
                opt.RequiredColumns = new List<string>
                {
                    "islem_tarihi",
                    "islem_tutari",
                    "aciklama"
                    // İstersen sonra: "islem_adi", "islem_sonrasi_bakiye" zorunlu yapılabilir
                };
                break;

            case ImportFileKind.KasaUstRapor:
                // Şimdilik boş
                break;

            case ImportFileKind.MasrafVeReddiyat:
                opt.RequiredColumns = new List<string>(); // Zorunlu kolon yok, esnek okuma

                opt.ColumnAliases["tip"] = new[] { "Tip", "Tür", "Tur", "Islem Tipi", "İşlem Tipi" };
                break;

            case ImportFileKind.OnlineHarcama:
            case ImportFileKind.OnlineMasraf:
            case ImportFileKind.OnlineReddiyat:
                opt.RequiredColumns = new List<string> { "dosya_no", "miktar" };
                break;
        }

        return opt;
    }

    /// <summary>
    /// True Source okuma için: satır limiti yok (null).
    /// Preview/UI test için GetOptions(kind) kullanılır (güvenli limitli).
    /// </summary>
    public static ExcelReadOptions GetTrueSourceOptions(ImportFileKind kind)
    {
        var opt = GetOptions(kind);
        opt.MaxRows = null; // ✅ limitsiz
        return opt;
    }

}
