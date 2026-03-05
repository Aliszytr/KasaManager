namespace KasaManager.Domain.Reports;

/// <summary>
/// Tek kaynak DTO: PDF (Genel/Özet), Excel, DB CRUD — hepsi buradan beslenir.
/// Ekranda gösterilen kartların birebir yansıması.
/// </summary>
public sealed class KasaRaporData
{
    // ══════════════════════════════════════════════════════
    // META
    // ══════════════════════════════════════════════════════
    public DateOnly Tarih { get; set; }
    public string KasaTuru { get; set; } = "";
    public string? KasayiYapan { get; set; }
    public string? Aciklama { get; set; }

    /// <summary>
    /// Günlük Kasa Notu: Kullanıcının girdiği özel notlar, uyarılar, açıklamalar.
    /// </summary>
    public string? GunlukNot { get; set; }

    /// <summary>
    /// Muhabere Numarası: Banka yazılarında kullanılan serbest formatlı sayı numarası.
    /// Örn: "2026/0001", "MUH/2026-15" vb.
    /// </summary>
    public string? MuhabereNo { get; set; }

    // ══════════════════════════════════════════════════════
    // KASA ÜST RAPOR (Genel Raporda kullanılır)
    // ══════════════════════════════════════════════════════
    public List<string> UstRaporKolonlar { get; set; } = new();
    public List<UstRaporSatir> UstRaporSatirlar { get; set; } = new();

    // ══════════════════════════════════════════════════════
    // ROW 1: Dünden Devreden + Genel Kasa
    // ══════════════════════════════════════════════════════
    public decimal DundenDevredenKasa { get; set; }
    public decimal GenelKasa { get; set; }

    // ══════════════════════════════════════════════════════
    // ROW 2: Reddiyat + Bankadan Çıkan + Stopaj Kontrol
    // ══════════════════════════════════════════════════════
    public decimal OnlineReddiyat { get; set; }
    public decimal BankadanCikan { get; set; }
    /// <summary>Kullanıcının manuel girdiği bankadan çekilen tutar (pool key: bankadan_cekilen)</summary>
    public decimal BankadanCekilen { get; set; }
    public decimal ToplamStopaj { get; set; }
    public bool StopajKontrolOk { get; set; }
    public decimal StopajKontrolFark { get; set; }

    // ══════════════════════════════════════════════════════
    // ROW 3: Bankaya Götürülecek
    // ══════════════════════════════════════════════════════
    public decimal BankayaStopaj { get; set; }
    public decimal BankayaTahsilat { get; set; }
    public decimal BankayaHarc { get; set; }
    public decimal BankayaToplam { get; set; }
    public decimal NakitToplam => BankayaToplam - BankayaStopaj;

    // IBAN bilgileri
    public string? HesapAdiStopaj { get; set; }
    public string? IbanStopaj { get; set; }
    public string? HesapAdiTahsilat { get; set; }
    public string? IbanTahsilat { get; set; }
    public string? HesapAdiHarc { get; set; }
    public string? IbanHarc { get; set; }

    // ══════════════════════════════════════════════════════
    // ROW 3B: Kasa & Banka Devir
    // ══════════════════════════════════════════════════════
    public decimal KasadakiNakit { get; set; }
    public decimal DundenDevredenBanka { get; set; }
    public decimal YarinaDevredecekBanka { get; set; }

    // ══════════════════════════════════════════════════════
    // ROW 4: Vergi Bilgileri
    // ══════════════════════════════════════════════════════
    public decimal VergidenGelen { get; set; }
    public decimal VergiKasa { get; set; }
    public decimal VergideBirikenKasa { get; set; }
    public List<string> VergiCalisanlari { get; set; } = new();

    // ══════════════════════════════════════════════════════
    // ROW 4B: Banka Reconciliation (Muhasebe Detayları)
    // ══════════════════════════════════════════════════════
    public decimal BankaGirenTahsilat { get; set; }
    public decimal BankaGirenHarc { get; set; }
    public decimal OnlineTahsilat { get; set; }
    public decimal OnlineHarc { get; set; }

    // Bankaya Beklenen Olağan Girişler (Fazla Gelenler)
    public decimal EftOtomatikIade { get; set; }
    public decimal GelenHavale { get; set; }
    public decimal IadeKelimesiGiris { get; set; }

    // ══════════════════════════════════════════════════════
    // ROW 4C: Eksik/Fazla (Sabah Kasa only)
    // ══════════════════════════════════════════════════════
    public bool IsSabahKasa { get; set; }

    // Tahsilat
    public decimal GuneAitEksikFazlaTahsilat { get; set; }
    public decimal DundenEksikFazlaTahsilat { get; set; }
    public decimal DundenEksikFazlaGelenTahsilat { get; set; }

    // Harç
    public decimal GuneAitEksikFazlaHarc { get; set; }
    public decimal DundenEksikFazlaHarc { get; set; }
    public decimal DundenEksikFazlaGelenHarc { get; set; }

    // ══════════════════════════════════════════════════════
    // ROW 4D: Bankaya Yatırılacak Doğrulama (Sabah Kasa)
    // ══════════════════════════════════════════════════════
    public decimal BankaMevduatTahsilat { get; set; }   // BankaTahsilat.xlsx: İşlem Adı = "Mevduata Para Yatırma"
    public decimal BankaVirmanTahsilat { get; set; }    // BankaTahsilat.xlsx: İşlem Adı = "Virman"
    public decimal BankaMevduatHarc { get; set; }       // BankaHarc.xlsx: İşlem Adı = "Mevduata Para Yatırma"
}

/// <summary>
/// Kasa Üst Rapor tablosunun tek satırı. 
/// Key=kolonAdı, Value=değer metin olarak.
/// </summary>
public sealed class UstRaporSatir
{
    public string VeznedarAdi { get; set; } = "";
    public Dictionary<string, string?> Degerler { get; set; } = new();
}
