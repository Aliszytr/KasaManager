#nullable enable
namespace KasaManager.Domain.Validation;

/// <summary>
/// Uyarı önem derecesi.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>Bilgi — kullanıcı bilgilendirilir, işlem engellenmez</summary>
    Info = 0,
    /// <summary>Uyarı — dikkat gerektirir ama işlem yapılabilir</summary>
    Warning = 1,
    /// <summary>Hata — ciddi tutarsızlık, kullanıcı dikkatlice kontrol etmeli</summary>
    Error = 2
}

/// <summary>
/// Hesaplama sonrası tespit edilen tek bir uyarı kaydı.
/// </summary>
public sealed record KasaValidationResult(
    /// <summary>Info, Warning veya Error</summary>
    ValidationSeverity Severity,

    /// <summary>Kuralın benzersiz kodu (MUTABAKAT_FARK_YUKSEK vb.)</summary>
    string Code,

    /// <summary>Kullanıcıya gösterilecek açıklama</summary>
    string Message,

    /// <summary>İlgili canonical key (varsa)</summary>
    string? Field = null,

    /// <summary>Sorunlu değer (varsa)</summary>
    decimal? Value = null
);
