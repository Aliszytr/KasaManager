#nullable enable
using KasaManager.Domain.FormulaEngine;

namespace KasaManager.Domain.Reports.Snapshots;

/// <summary>
/// R17: Hesaplanmış Kasa değerlerinin VT kaydı.
/// Preview'da hesaplanan sonuçlar buraya yazılır.
/// Tarih + Kasa Tipi ile sorgulanabilir.
/// Soft delete + versioning ile audit trail sağlanır.
/// </summary>
public sealed class CalculatedKasaSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>Hangi gün için hesaplandı</summary>
    public DateOnly RaporTarihi { get; set; }
    
    /// <summary>Kasa türü (Aksam=2, Sabah=1, Genel=0, Ortak=3)</summary>
    public KasaRaporTuru KasaTuru { get; set; }
    
    /// <summary>Kullanılan formül seti ID'si</summary>
    public Guid? FormulaSetId { get; set; }
    
    /// <summary>Formül seti adı (kayıt anındaki)</summary>
    public string? FormulaSetName { get; set; }
    
    /// <summary>Hesaplama anı (UTC)</summary>
    public DateTime CalculatedAtUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>Hesaplamayı yapan kullanıcı</summary>
    public string? CalculatedBy { get; set; }
    
    /// <summary>Versiyon (aynı tarih+kasa için birden fazla kayıt olabilir)</summary>
    public int Version { get; set; } = 1;
    
    /// <summary>Aktif/Geçerli mi? (En son hesaplama)</summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>Silinmiş mi? (Soft delete için)</summary>
    public bool IsDeleted { get; set; } = false;
    
    /// <summary>Silme zamanı</summary>
    public DateTime? DeletedAtUtc { get; set; }
    
    /// <summary>Silen kullanıcı</summary>
    public string? DeletedBy { get; set; }
    
    /// <summary>Input değerleri JSON (ham veriler - formüle giren)</summary>
    public string InputsJson { get; set; } = "{}";
    
    /// <summary>Output değerleri JSON (hesaplanmış sonuçlar)</summary>
    public string OutputsJson { get; set; } = "{}";
    
    /// <summary>Kullanıcı tarafından verilen rapor adı (opsiyonel)</summary>
    public string? Name { get; set; }
    
    /// <summary>Kısa açıklama / rapor notu</summary>
    public string? Description { get; set; }
    
    /// <summary>Kullanıcı notları (günlük özel bilgiler, uyarılar vb.)</summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// KasaRaporData DTO'sunun tam JSON'ı.
    /// Ekrandaki TÜM verilerin birebir kaydı — re-hydration için.
    /// </summary>
    public string? KasaRaporDataJson { get; set; }

    /// <summary>
    /// Faz 3: Snapshot anındaki Financial Exceptions özet verisi (JSON).
    /// Bilgi amaçlıdır — hesap motorunun yerine geçmez.
    /// </summary>
    public string? FinancialExceptionsSummaryJson { get; set; }
    
    // ══════════════════════════════════════════════════════
    // Helper Methods
    // ══════════════════════════════════════════════════════
    
    /// <summary>
    /// Input'ları dictionary olarak getir.
    /// R19: Eksik alanlar otomatik olarak varsayılan değerle doldurulur.
    /// OB-5 FIX: Bare catch → JsonException. Kritik hatalar (OutOfMemory vb.) yutulmuyor.
    /// </summary>
    public Dictionary<string, decimal> GetInputs()
    {
        Dictionary<string, decimal>? parsed = null;
        
        if (!string.IsNullOrWhiteSpace(InputsJson) && InputsJson != "{}")
        {
            try
            {
                parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(InputsJson);
            }
            catch (System.Text.Json.JsonException)
            {
                // Bozuk JSON — varsayılanlara düş. Kritik hatalar (OutOfMemory vb.) propagate olur.
                System.Diagnostics.Debug.WriteLine($"[CalculatedKasaSnapshot] InputsJson parse başarısız: {InputsJson?[..Math.Min(100, InputsJson?.Length ?? 0)]}");
            }
        }
        
        // R19: Eksik alanları varsayılan değerle doldur
        return MissingFieldHandler.EnsureAllDecimalFields(parsed);
    }
    
    /// <summary>
    /// Output'ları dictionary olarak getir.
    /// R19: Eksik alanlar otomatik olarak varsayılan değerle doldurulur.
    /// OB-5 FIX: Bare catch → JsonException. Kritik hatalar (OutOfMemory vb.) yutulmuyor.
    /// CRUD-FIX: OutputsJson hem Dict&lt;string,decimal&gt; hem Dict&lt;string,string&gt; formatını destekler.
    /// </summary>
    public Dictionary<string, decimal> GetOutputs()
    {
        Dictionary<string, decimal>? parsed = null;
        
        if (!string.IsNullOrWhiteSpace(OutputsJson) && OutputsJson != "{}")
        {
            try
            {
                parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(OutputsJson);
            }
            catch (System.Text.Json.JsonException)
            {
                // Fallback: string value formatını destekle (ExtractOutputsForSnapshot "F2" format)
                try
                {
                    var stringDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(OutputsJson);
                    if (stringDict != null)
                    {
                        parsed = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in stringDict)
                            if (decimal.TryParse(kv.Value, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var d))
                                parsed[kv.Key] = d;
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    System.Diagnostics.Debug.WriteLine($"[CalculatedKasaSnapshot] OutputsJson parse başarısız: {OutputsJson?[..Math.Min(100, OutputsJson?.Length ?? 0)]}");
                }
            }
        }
        
        // R19: Eksik alanları varsayılan değerle doldur
        return MissingFieldHandler.EnsureAllDecimalFields(parsed);
    }
    
    /// <summary>Input'ları JSON olarak kaydet</summary>
    public void SetInputs(IDictionary<string, decimal> inputs)
    {
        InputsJson = System.Text.Json.JsonSerializer.Serialize(inputs);
    }
    
    /// <summary>Output'ları JSON olarak kaydet</summary>
    public void SetOutputs(IDictionary<string, decimal> outputs)
    {
        OutputsJson = System.Text.Json.JsonSerializer.Serialize(outputs);
    }
    
    /// <summary>Görüntüleme için özet bilgi</summary>
    public string GetDisplaySummary()
    {
        var outputs = GetOutputs();
        var genelKasa = outputs.TryGetValue("genel_kasa", out var gk) ? gk : 0;
        return $"{KasaTuru} - {RaporTarihi:dd.MM.yyyy} v{Version} - Genel Kasa: {genelKasa:N2}";
    }
}
