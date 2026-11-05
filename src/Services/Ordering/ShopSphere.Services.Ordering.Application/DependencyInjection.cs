using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ShopSphere.Services.Ordering.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOrderingApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
        });

        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}

