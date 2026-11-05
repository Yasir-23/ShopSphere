using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopSphere.Services.Ordering.Application.Repositories;
using ShopSphere.Services.Ordering.Infrastructure.Persistence;
using ShopSphere.Services.Ordering.Infrastructure.Repositories;

namespace ShopSphere.Services.Ordering.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrderingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1. SQL Server Persistence
        var connectionString = configuration.GetConnectionString("OrderingDb")
            ?? "Server=localhost,11433;Database=ShopSphere_Ordering;User Id=sa;Password=ShopSphere_Sql2022!;TrustServerCertificate=True;MultipleActiveResultSets=True;";

        services.AddDbContext<OrderingDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
        });

        services.AddScoped<IOrderRepository, OrderRepository>();

        // 2. MassTransit with RabbitMQ
        services.AddMassTransit(busConfigurator =>
        {
            busConfigurator.SetKebabCaseEndpointNameFormatter();

            busConfigurator.UsingRabbitMq((context, cfg) =>
            {
                var host = configuration["RabbitMQ:Host"]
                    ?? Environment.GetEnvironmentVariable("RABBITMQ_HOST")
                    ?? "localhost";
                var portString = configuration["RabbitMQ:Port"]
                    ?? Environment.GetEnvironmentVariable("RABBITMQ_PORT")
                    ?? "15672";
                var username = configuration["RabbitMQ:Username"]
                    ?? Environment.GetEnvironmentVariable("RABBITMQ_USERNAME")
                    ?? Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_USER")
                    ?? "shopsphere";
                var password = configuration["RabbitMQ:Password"]
                    ?? Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD")
                    ?? Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_PASS")
                    ?? "ShopSphere_RabbitMQ_2026!";

                ushort port = 5672;
                if (ushort.TryParse(portString, out var p))
                {
                    port = p;
                }

                cfg.Host(host, port, "/", h =>
                {
                    h.Username(username);
                    h.Password(password);
                });

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}

