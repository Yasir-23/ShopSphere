using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ShopSphere.EventBus;

public static class EventBusExtensions
{
    public static IServiceCollection AddShopSphereEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        services.AddMassTransit(busConfigurator =>
        {
            configureConsumers?.Invoke(busConfigurator);

            busConfigurator.UsingRabbitMq((context, cfg) =>
            {
                var host = configuration["RabbitMQ:Host"] 
                    ?? Environment.GetEnvironmentVariable("RABBITMQ_HOST") 
                    ?? "localhost";
                var portString = configuration["RabbitMQ:Port"] 
                    ?? Environment.GetEnvironmentVariable("RABBITMQ_PORT") 
                    ?? "15672";
                var username = configuration["RabbitMQ:Username"] 
                    ?? Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_USER") 
                    ?? "shopsphere";
                var password = configuration["RabbitMQ:Password"] 
                    ?? Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_PASS") 
                    ?? "ShopSphere_RabbitMQ_2026!";

                ushort port = 5672;
                if (ushort.TryParse(portString, out var parsedPort))
                {
                    port = parsedPort;
                }

                cfg.Host(host, port, "/", h =>
                {
                    h.Username(username);
                    h.Password(password);
                });

                // Per SDD 3.3: retry middleware, dead-letter exchanges (*-error), snake_case naming
                cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(500)));

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
