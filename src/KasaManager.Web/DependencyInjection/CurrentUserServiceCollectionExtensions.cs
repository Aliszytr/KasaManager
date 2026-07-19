using KasaManager.Application.Abstractions;
using KasaManager.Web.Services;

namespace KasaManager.Web.DependencyInjection;

public static class CurrentUserServiceCollectionExtensions
{
    public static IServiceCollection AddCurrentUser(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        return services;
    }
}

