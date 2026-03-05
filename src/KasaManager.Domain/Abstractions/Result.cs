namespace KasaManager.Domain.Abstractions;

/// <summary>
/// MS5: Zenginleştirilmiş Result monadı.
/// Geriye uyumlu: .Ok, .Error, .Value korunuyor.
/// Eklenenler: Errors list, Warnings list, non-generic Result.
/// </summary>
public sealed class Result<T>
{
    public bool Ok { get; }
    public string? Error { get; }
    public T? Value { get; }

    /// <summary>Tüm hata mesajları (Error dahil).</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Başarılı ama dikkat gerektiren uyarılar.</summary>
    public IReadOnlyList<string> Warnings { get; }

    private Result(bool ok, T? value, string? error,
        IReadOnlyList<string>? errors = null, IReadOnlyList<string>? warnings = null)
    {
        Ok = ok;
        Value = value;
        Error = error;
        Errors = errors ?? (error != null ? new[] { error } : Array.Empty<string>());
        Warnings = warnings ?? Array.Empty<string>();
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Fail(string error) => new(false, default, error);
    public static Result<T> Fail(string error, IReadOnlyList<string> errors) => new(false, default, error, errors);

    /// <summary>Başarılı sonuca uyarı ekler.</summary>
    public Result<T> WithWarnings(IEnumerable<string> warnings)
    {
        var list = warnings?.ToList() ?? new List<string>();
        return new Result<T>(Ok, Value, Error, Errors, list);
    }
}

/// <summary>
/// MS5: Değer döndürmeyen operasyonlar için non-generic Result.
/// </summary>
public sealed class Result
{
    public bool Ok { get; }
    public string? Error { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<string> Warnings { get; }

    private Result(bool ok, string? error,
        IReadOnlyList<string>? errors = null, IReadOnlyList<string>? warnings = null)
    {
        Ok = ok;
        Error = error;
        Errors = errors ?? (error != null ? new[] { error } : Array.Empty<string>());
        Warnings = warnings ?? Array.Empty<string>();
    }

    public static Result Success() => new(true, null);
    public static Result Fail(string error) => new(false, error);
    public static Result Fail(string error, IReadOnlyList<string> errors) => new(false, error, errors);
}
