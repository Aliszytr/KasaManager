using KasaManager.Application.Processing.Abstractions.Repositories;
using KasaManager.Application.Processing.Models;

namespace KasaManager.Infrastructure.Processing.InMemory;

public sealed class InMemoryOnlineMasrafNesnesiRepository : InMemoryReportRepository<MasrafveReddiyatOkumaNesnesi>, IOnlineMasrafNesnesiRepository
{
}
