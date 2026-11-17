
using Microsoft.Extensions.DependencyInjection;

namespace Xumora.Photography.Application.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddPhotographyApplication(
        this IServiceCollection services)
    {
        // Register Application services here.
        // Example: use cases, validators and handlers.

        return services;
    }
}
