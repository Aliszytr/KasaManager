using KasaManager.Domain.Calculation;

namespace KasaManager.Web.Models;

/// <summary>
/// R16 Formula Designer ViewModel — şablon yönetimi + canlı test.
/// </summary>
public class FormulaDesignerViewModel
{
    // ── Şablon Seçimi ──
    public string? SelectedTemplateId { get; set; }
    public string TemplateName { get; set; } = "";
    public string ScopeType { get; set; } = "Custom";

    // ── Şablon Listesi ──
    public List<TemplateListItem> Templates { get; set; } = new();

    // ── Formül Satırları ──
    public List<FormulaDesignerRow> Rows { get; set; } = new();

    // ── Pool Veri Önizleme ──
    public List<PoolEntryDisplay> PoolEntries { get; set; } = new();

    // ── Canlı Test ──
    public DateOnly? TestDate { get; set; }
    public bool HasTestResults { get; set; }
    public Dictionary<string, decimal> TestResults { get; set; } = new();
    public List<FormulaExplainItem> TestExplain { get; set; } = new();

    // ── Kullanıcı Girişleri (Manuel değerler — formüllere override olarak girer) ──
    public decimal? BozukPara { get; set; }
    public decimal? NakitPara { get; set; }
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

    /// <summary>
    /// Kullanıcı girişleri paneli açık/kapalı durum izleyicisi.
    /// </summary>
    public bool ShowUserInputs { get; set; }

    // ── Mesajlar ──
    public List<string> Errors { get; set; } = new();
    public List<string> Infos { get; set; } = new();
}

public class TemplateListItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ScopeType { get; set; } = "";
    public bool IsActive { get; set; }
    public int LineCount { get; set; }
}

public class FormulaDesignerRow
{
    public int Index { get; set; }
    public string RowId { get; set; } = "";
    public string TargetKey { get; set; } = "";
    public string Mode { get; set; } = "Map";
    public string SourceKey { get; set; } = "";
    public string Expression { get; set; } = "";
    public bool IsHidden { get; set; }
}

public class PoolEntryDisplay
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Source { get; set; } = "";
    public string Type { get; set; } = "";
}

public class FormulaExplainItem
{
    public string TargetKey { get; set; } = "";
    public string Expression { get; set; } = "";
    public decimal Result { get; set; }
}
