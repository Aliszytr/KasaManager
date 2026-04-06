using System.Threading;

namespace KasaManager.Application.Observability;

public static class ShadowMetrics
{
    private static long _missingInputCount;
    private static long _successCount;

    public static long MissingInputCount => Interlocked.Read(ref _missingInputCount);
    public static long SuccessCount => Interlocked.Read(ref _successCount);

    public static void IncrementMissingInput()
    {
        Interlocked.Increment(ref _missingInputCount);
    }

    public static void IncrementSuccess()
    {
        Interlocked.Increment(ref _successCount);
    }
}
