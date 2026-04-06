namespace KasaManager.Application.Services;

/// <summary>
/// P2.6: Post-switch monitoring and hardening metrics.
/// Tracks clean runs, fallbacks, and explicit legacy calls.
/// Thread-safe via Interlocked.
/// </summary>
public static class EksikFazlaMetrics
{
    private static int _projectionCleanCount;
    private static int _projectionFallbackCount;
    private static int _projectionExceptionCount;
    private static int _shadowCallCount;
    private static int _legacyExplicitCount;

    public static int ProjectionCleanCount => _projectionCleanCount;
    public static int ProjectionFallbackCount => _projectionFallbackCount;
    public static int ProjectionExceptionCount => _projectionExceptionCount;
    public static int ShadowCallCount => _shadowCallCount;
    public static int LegacyExplicitCount => _legacyExplicitCount;

    public static void RecordClean() => Interlocked.Increment(ref _projectionCleanCount);
    public static void RecordFallback() => Interlocked.Increment(ref _projectionFallbackCount);
    public static void RecordException() => Interlocked.Increment(ref _projectionExceptionCount);
    public static void RecordShadow() => Interlocked.Increment(ref _shadowCallCount);
    public static void RecordLegacy() => Interlocked.Increment(ref _legacyExplicitCount);

    public static void Reset()
    {
        _projectionCleanCount = 0;
        _projectionFallbackCount = 0;
        _projectionExceptionCount = 0;
        _shadowCallCount = 0;
        _legacyExplicitCount = 0;
    }
}
