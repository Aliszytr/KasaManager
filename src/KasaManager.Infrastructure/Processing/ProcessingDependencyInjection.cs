using KasaManager.Application.Processing.Abstractions.Services;
using KasaManager.Application.Processing.Abstractions.Repositories;
using KasaManager.Infrastructure.Processing.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace KasaManager.Infrastructure;

public static class ProcessingDependencyInjection
{
    /// <summary>
    /// R4: DB yokken dataset store için InMemory workspace.
    /// </summary>
    public static IServiceCollection AddProcessingWorkspaceInMemory(this IServiceCollection services)
    {
        // Dataset repository'leri (Legacy - mevcut kodla uyumluluk için korunuyor)
        services.AddSingleton<IAksamKasaNesnesiRepository, InMemoryAksamKasaNesnesiRepository>();
        services.AddSingleton<ISabahKasaNesnesiRepository, InMemorySabahKasaNesnesiRepository>();
        services.AddSingleton<IGenelKasaRaporNesnesiRepository, InMemoryGenelKasaRaporNesnesiRepository>();
        services.AddSingleton<IBankaVerilerNesnesiRepository, InMemoryBankaVerilerNesnesiRepository>();
        services.AddSingleton<IUYAPVerilerNesnesiRepository, InMemoryUYAPVerilerNesnesiRepository>();

        // REFACTOR R1: Unified Kasa Repository - tüm kasa tiplerini tek repository'de yönetir
        services.AddSingleton<IUnifiedKasaRepository, InMemoryUnifiedKasaRepository>();

        // Workspace
        services.AddSingleton<IProcessingWorkspace, InMemoryProcessingWorkspace>();
        return services;
    }
}
