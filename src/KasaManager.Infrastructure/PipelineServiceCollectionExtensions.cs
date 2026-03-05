#nullable enable
using Microsoft.Extensions.DependencyInjection;
using KasaManager.Application.Pipeline;

namespace KasaManager.Infrastructure;

/// <summary>
/// R20 Wave 5: Pipeline DI Registration.
/// Tüm pipeline servislerini DI konteynerine ekler.
/// </summary>
public static class PipelineServiceCollectionExtensions
{
    /// <summary>
    /// R20 Formula Engine Pipeline servislerini kaydeder.
    /// Program.cs'de çağrılmalı: builder.Services.AddFormulaPipeline();
    /// </summary>
    public static IServiceCollection AddFormulaPipeline(this IServiceCollection services)
    {
        // Core Pipeline
        services.AddScoped<IDataPipeline, UnifiedDataPipeline>();
        
        // Context Factory
        services.AddScoped<IFormulaContextFactory, FormulaContextFactory>();
        
        return services;
    }
}
