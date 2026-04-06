using System.Threading.Tasks;

namespace KasaManager.Application.Observability;

public interface IAlertService
{
    Task TriggerAsync(string code, string message);
}
