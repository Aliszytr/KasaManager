#nullable enable
namespace KasaManager.Domain.FormulaEngine;

/// <summary>
/// R17B: Alan veri kaynağı türü.
/// Field Chooser'da kaynak bazlı gruplama için kullanılır.
/// </summary>
public enum FieldSource
{
    /// <summary>Excel dosyasından okunan ham veri (Normal, Online, PTT tahsilatlar vb.)</summary>
    Excel = 0,
    
    /// <summary>Kullanıcı tarafından manuel girilen değer (Bozuk para, devreden kasa vb.)</summary>
    UserInput = 1,
    
    /// <summary>Formül engine tarafından hesaplanan değer veya model alanları (Toplam, Fark, Bakiye vb.)</summary>
    Calculated = 2
}
