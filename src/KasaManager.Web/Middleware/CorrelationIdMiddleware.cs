using System.Diagnostics;

namespace KasaManager.Web.Middleware;

/// <summary>
/// MS9: Her HTTP request'e benzersiz bir Correlation ID atar.
/// - Request header'da X-Correlation-ID varsa onu kullanır,
/// - Yoksa yeni GUID üretir.
/// - Response header'a X-Correlation-ID olarak yazar.
/// - ILogger scope'una ekler — tüm loglar bu ID ile ilişkilendirilir.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        // 1. Header'dan oku veya yeni oluştur
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N")[..12]; // Kısa, okunabilir ID
        }

        // 2. HttpContext.TraceIdentifier'a da ata (built-in logging ile uyum)
        context.TraceIdentifier = correlationId;

        // 3. Response header'a yaz (client tarafında debug/destek için)
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // 4. Log scope'una ekle — bu scope aktifken tüm loglar CorrelationId içerir
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await _next(context);
        }
    }
}

/// <summary>
/// Extension method: app.UseCorrelationId()
/// </summary>
public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}
