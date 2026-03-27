using KasaManager.Application.Processing.Abstractions.Repositories;
using KasaManager.Application.Processing.Models;

namespace KasaManager.Infrastructure.Processing.InMemory;

public sealed class InMemoryBankaVerilerNesnesiRepository : InMemoryReportRepository<BankaVerilerNesnesi>, IBankaVerilerNesnesiRepository
{
}
