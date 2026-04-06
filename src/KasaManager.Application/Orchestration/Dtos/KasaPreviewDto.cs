using KasaManager.Domain.Calculation;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;
using KasaManager.Application.Abstractions;


namespace KasaManager.Application.Orchestration.Dtos;

public sealed class KasaPreviewDto
{
    public DateOnly? SelectedDate { get; set; }

    // Intent-First: Hangi kasa türüyle çalışıldığı (Sabah/Aksam/Genel/Ortak/Custom)
    public string KasaType { get; set; } = "";

    // Admin modu: R16 Designer görünürlüğü
    public bool IsAdminMode { get; set; }

    // Akşam Kasa: Mesai Sonu modunda sadece 4 dosya okunur
    public bool AksamMesaiSonuModu { get; set; } = true;

    // Hesaplama sonrası sonuçların mevcut olup olmadığı
    public bool HasResults { get; set; }

    // R10 Metadata
    public DateOnly? GenelKasaStartDate { get; set; }
    public DateOnly? GenelKasaEndDate { get; set; }
    public string? GenelKasaStartDateSource { get; set; }
    public string? GenelKasaEndDateSource { get; set; }

    public bool IsDataLoaded { get; set; }
    // P4.3: HasSnapshot removed

    // Options
    public List<string> VeznedarOptions { get; set; } = new();
    public List<string> VergiKasaVeznedarlar { get; set; } = new();
    public decimal VergiKasaBakiyeToplam { get; set; }

    // Defaults
    public decimal? DefaultBozukPara { get; set; }
    public decimal? DefaultNakitPara { get; set; }
    public decimal? DefaultGenelKasaDevredenSeed { get; set; }
    public DateOnly? DefaultGenelKasaBaslangicTarihiSeed { get; set; }
    public decimal? DefaultKaydenTahsilat { get; set; }
    public decimal? DefaultDundenDevredenKasaNakit { get; set; }

    // Inputs (Calculated or Manual)
    public decimal? VergidenGelen { get; set; }
    public decimal? GelmeyenD { get; set; }

    public decimal? KasadaKalacakHedef { get; set; }
    public decimal? KaydenTahsilat { get; set; }
    public decimal? KaydenHarc { get; set; }
    public decimal? BankadanCekilen { get; set; }
    public decimal? CesitliNedenlerleBankadanCikamayanTahsilat { get; set; }
    public decimal? BankayaGonderilmisDeger { get; set; }
    public decimal? BankayaYatirilacakHarciDegistir { get; set; }
    public decimal? BankayaYatirilacakTahsilatiDegistir { get; set; }

    // Eksik/Fazla Kullanıcı Girişleri
    public decimal? GuneAitEksikFazlaTahsilat { get; set; }
    public decimal? GuneAitEksikFazlaHarc { get; set; }
    public decimal? DundenEksikFazlaTahsilat { get; set; }
    public decimal? DundenEksikFazlaHarc { get; set; }
    public decimal? DundenEksikFazlaGelenTahsilat { get; set; }
    public decimal? DundenEksikFazlaGelenHarc { get; set; }

    public string? KasayiYapan { get; set; }
    public string? Aciklama { get; set; }
    public decimal? BozukPara { get; set; }
    public decimal? NakitPara { get; set; }
    public decimal? VergideBirikenKasa { get; set; }

    // Outputs
    public KasaDraftBundle? Drafts { get; set; }
    public List<UnifiedPoolEntry> PoolEntries { get; set; } = new();
    
    // Designer / Test Scope
    public DateOnly? DesignerStartDate { get; set; }
    public DateOnly? DesignerEndDate { get; set; }
    public string? InputCatalogScopeLabel { get; set; }

    // UI Feedback
    public string? DbInfoMessage { get; set; }

    // UI Catalog & Mappings

    public List<KasaInputCatalogEntry> InputCatalog { get; set; } = new();
    public List<string> SelectedInputKeys { get; set; } = new();
    // DB / Formula Set Metadata
    public string? DbFormulaSetId { get; set; }
    public string? DbFormulaSetName { get; set; }
    public string? DbScopeType { get; set; }
    public List<FormulaSetListItem> DbFormulaSets { get; set; } = new();

    // Mappings & Suggestions
    public List<KasaPreviewMappingRow> Mappings { get; set; } = new();
    public List<string> TargetKeySuggestions { get; set; } = new(); 



    // Formula Engine
    public string? SelectedFormulaSetId { get; set; }
    public List<ParityDiffItem> ParityDiffs { get; set; } = new();

    public List<FormulaSet> AvailableFormulaSets { get; set; } = new();
    public CalculationRun? FormulaRun { get; set; }

    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
