using System;

namespace KasaManager.Domain.Calculation.Data;

/// <summary>
/// Belirli bir tarih ve kasa türü için hesaplanan aktif ve en güncel sonuçtur.
/// Eski sistemde CalculatedKasaSnapshot'ın Live karşılığıdır.
/// </summary>
public class DailyCalculationResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly ForDate { get; set; }
    public string KasaTuru { get; set; } = string.Empty; // Aksam, Sabah, Genel vb.
    public Guid FormulaSetId { get; set; }
    
    // Versiyon Bağlamı
    public string NormalizationVersion { get; set; } = string.Empty;
    public string CalculationEngineVersion { get; set; } = string.Empty;
    public string CarryOverPolicyVersion { get; set; } = string.Empty;
    
    public int CalculatedVersion { get; set; } = 1;
    
    // Dünün devredeni veya bugünün fact leri değiştiyse True olur. Manual Rebuild tetikleyicisi.
    public bool IsStale { get; set; }
    
    // Geçmiş günün kilitli (locked) olma durumu (önceki aylar vb.)
    public bool IsLocked { get; set; }
    
    // Devreden zinciri takibi için dünkü hesabın ID'si
    public Guid? PreviousResultId { get; set; }
    
    public string InputsFingerprint { get; set; } = string.Empty;
    public string ResultsJson { get; set; } = "{}";
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
}
