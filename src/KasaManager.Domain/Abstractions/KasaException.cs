namespace KasaManager.Domain.Abstractions;

/// <summary>
/// Tüm KasaManager domain exception'larının temel sınıfı.
/// Generic Exception yerine bu sınıftan türetilen exception'lar kullanılır.
/// </summary>
public class KasaException : Exception
{
    public string? ErrorCode { get; }

    public KasaException(string message, string? errorCode = null)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public KasaException(string message, Exception innerException, string? errorCode = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Varlık bulunamadığında fırlatılır (404 karşılığı).
/// </summary>
public class KasaNotFoundException : KasaException
{
    public string EntityName { get; }
    public object? EntityId { get; }

    public KasaNotFoundException(string entityName, object? entityId = null)
        : base($"'{entityName}' bulunamadı{(entityId != null ? $" (Id: {entityId})" : "")}.", "NOT_FOUND")
    {
        EntityName = entityName;
        EntityId = entityId;
    }
}

/// <summary>
/// İş kuralı ihlal edildiğinde fırlatılır (422 karşılığı).
/// </summary>
public class KasaValidationException : KasaException
{
    public IReadOnlyList<string> ValidationErrors { get; }

    public KasaValidationException(string message)
        : base(message, "VALIDATION_ERROR")
    {
        ValidationErrors = new[] { message };
    }

    public KasaValidationException(IEnumerable<string> errors)
        : base(string.Join("; ", errors), "VALIDATION_ERROR")
    {
        ValidationErrors = errors.ToList().AsReadOnly();
    }
}

/// <summary>
/// Excel dosya okuma hatalarında fırlatılır.
/// </summary>
public class KasaExcelException : KasaException
{
    public string? FileName { get; }
    public string? SheetName { get; }

    public KasaExcelException(string message, string? fileName = null, string? sheetName = null, Exception? inner = null)
        : base(message, inner!, "EXCEL_ERROR")
    {
        FileName = fileName;
        SheetName = sheetName;
    }
}

/// <summary>
/// Eşzamanlılık (concurrency) hatalarında fırlatılır.
/// </summary>
public class KasaConcurrencyException : KasaException
{
    public KasaConcurrencyException(string message, Exception? inner = null)
        : base(message, inner!, "CONCURRENCY_ERROR")
    {
    }
}
