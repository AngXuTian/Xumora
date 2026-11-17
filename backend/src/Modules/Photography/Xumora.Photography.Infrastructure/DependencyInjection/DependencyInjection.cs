
using Microsoft.Extensions.DependencyInjection;

namespace Xumora.Photography.Infrastructure.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddPhotographyInfrastructure(
        this IServiceCollection services)
    {
        // Register Infrastructure implementations here.
        // Example: repositories, external AI providers and storage.

        return services;
    }
}
