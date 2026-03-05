using System.Net;
using System.Text.Json;
using KasaManager.Domain.Abstractions;

namespace KasaManager.Web.Middleware;

/// <summary>
/// Global hata yakalama middleware'i.
/// Tüm unhandled exception'ları yakalar ve uygun HTTP yanıtı döner.
/// AJAX/API istekleri için JSON, sayfa istekleri için hata sayfası döner.
/// </summary>
public sealed class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // İstemci bağlantıyı kapattı — normal durum, loglama gerekmez
            context.Response.StatusCode = 499; // Client Closed Request
        }
        catch (KasaNotFoundException ex)
        {
            _logger.LogWarning(ex, "Kaynak bulunamadı: {EntityName} (Id: {EntityId})", ex.EntityName, ex.EntityId);
            await WriteErrorResponseAsync(context, HttpStatusCode.NotFound, ex.Message, ex.ErrorCode);
        }
        catch (KasaValidationException ex)
        {
            _logger.LogWarning(ex, "Doğrulama hatası: {Message}", ex.Message);
            await WriteErrorResponseAsync(context, HttpStatusCode.UnprocessableEntity, ex.Message, ex.ErrorCode);
        }
        catch (KasaExcelException ex)
        {
            _logger.LogError(ex, "Excel okuma hatası: {FileName} / {SheetName}", ex.FileName, ex.SheetName);
            await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest, ex.Message, ex.ErrorCode);
        }
        catch (KasaConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Eşzamanlılık hatası: {Message}", ex.Message);
            await WriteErrorResponseAsync(context, HttpStatusCode.Conflict, ex.Message, ex.ErrorCode);
        }
        catch (KasaException ex)
        {
            _logger.LogError(ex, "KasaManager hatası: {Message}", ex.Message);
            await WriteErrorResponseAsync(context, HttpStatusCode.InternalServerError, ex.Message, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Beklenmeyen hata: {Path}", context.Request.Path);
            await WriteErrorResponseAsync(context, HttpStatusCode.InternalServerError,
                "Sunucu tarafında beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.", "INTERNAL_ERROR");
        }
    }

    private static async Task WriteErrorResponseAsync(HttpContext context, HttpStatusCode statusCode, string message, string? errorCode)
    {
        if (context.Response.HasStarted) return;

        context.Response.StatusCode = (int)statusCode;

        // AJAX istekleri için JSON döner
        if (IsAjaxRequest(context))
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            var error = new
            {
                success = false,
                error = message,
                code = errorCode ?? "UNKNOWN",
                statusCode = (int)statusCode
            };
            await context.Response.WriteAsync(JsonSerializer.Serialize(error, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            }));
        }
        else
        {
            // HTML sayfa istekleri için Error controller'a yönlendir
            context.Response.Redirect($"/Home/Error?message={Uri.EscapeDataString(message)}");
        }
    }

    private static bool IsAjaxRequest(HttpContext context)
    {
        return context.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
               context.Request.Headers.Accept.Any(a => a?.Contains("application/json") == true) ||
               context.Request.Path.StartsWithSegments("/api");
    }
}

/// <summary>
/// Middleware uzantı metodu.
/// </summary>
public static class GlobalExceptionHandlerExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
    }
}
