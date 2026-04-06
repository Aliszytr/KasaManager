using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace KasaManager.Application.Observability;

public class AlertService : IAlertService
{
    private readonly ILogger<AlertService> _logger;

    public AlertService(ILogger<AlertService> logger)
    {
        _logger = logger;
    }

    public Task TriggerAsync(string code, string message)
    {
        // Basit implementasyon: LogCritical ile alert yollar. İleride mail/slack eklenecek.
        _logger.LogCritical("ALERT [{Code}] triggered: {Message}", code, message);
        return Task.CompletedTask;
    }
}
