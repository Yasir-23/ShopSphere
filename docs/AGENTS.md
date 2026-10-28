# AGENTS.md — Autonomous Coding Agent Directives & Architecture Rules

## Document Details
- **Project Name:** ShopSphere E-Commerce Microservices Platform
- **Target Platform:** .NET 9 / C# 13 Distributed Microservices Architecture
- **Audience:** AI Coding Agents & Autonomous Code Generation Systems
- **Status:** Mandatory Strict Constraint Specification

---

## 1. Prime Directives for the Coding Agent

As an autonomous coding agent building ShopSphere, you MUST adhere to the following non-negotiable architectural mandates:

1. **Step-by-Step Discipline (Milestone Scoping):**
   - Implement ONLY what is requested in the active milestone prompt.
   - Do NOT scaffold future services, sagas, or components ahead of time.
   - Do NOT commit code to Git unless explicitly commanded with a specified commit message.

2. **Strict Database Isolation:**
   - NEVER create cross-database relationships, shared connection strings, or multi-database `DbContext` setups.
   - Every service is completely autonomous. If Service A needs data owned by Service B, obtain it via asynchronous events (MassTransit) or synchronous read contracts (gRPC).

3. **Production-Ready Code Only:**
   - NO mock placeholders (`throw new NotImplementedException()`, `// TODO: implement later`).
   - All code must compile cleanly with `TreatWarningsAsErrors=true` and `<Nullable>enable</Nullable>`.
   - Every endpoint must have complete input validation using **FluentValidation** and standard error output using **RFC 7807 ProblemDetails**.

---

## 2. Technology Stack & Framework Constraints

| Role | Technology Selection | Version / Constraints |
| :--- | :--- | :--- |
| **Language & SDK** | C# 13 / .NET 9 | Top-level statements, file-scoped namespaces, records, pattern matching |
| **Persistence (Relational)** | Microsoft SQL Server | EF Core 9 (Order & Identity Services) |
| **Persistence (Postgres)** | PostgreSQL 16 | EF Core 9 (writes) + Dapper (high-speed reads for Inventory & Payment) |
| **Persistence (Document)** | MongoDB 7.0 | MongoDB.Driver (Catalog Service) |
| **Persistence (Key-Value)** | Redis 7 | StackExchange.Redis (Basket Service) |
| **Message Broker** | RabbitMQ 3.13+ | MassTransit 8.2+ with EF Core Outbox/Inbox |
| **Edge Gateway** | YARP | Microsoft.ReverseProxy |
| **API Endpoints** | Minimal APIs | Carter library for modular endpoint definition |
| **Inter-Service RPC** | gRPC | Grpc.AspNetCore (HTTP/2 Protobuf) |
| **Resilience** | Polly v8 | Microsoft.Extensions.Resilience / Polly.Core pipelines |
| **Observability** | OpenTelemetry + Serilog | OTLP exporter to Jaeger (`4317`) + Seq Sink |
| **Testing** | xUnit + FluentAssertions | Testcontainers for Dockerized integration tests |

---

## 3. Standard Solution & Project Structure

The agent must maintain the following file and folder hierarchy when generating projects:

```
ShopSphere/
├── docs/                               <-- System architecture & specifications
├── src/
│   ├── BuildingBlocks/
│   │   ├── ShopSphere.Common.Logging/   <-- Serilog & OpenTelemetry setup
│   │   ├── ShopSphere.Common.Security/  <-- JWT validation & policy extensions
│   │   ├── ShopSphere.Common.Resilience/<-- Polly v8 pipeline definitions
│   │   └── ShopSphere.EventBus/         <-- MassTransit configurations & shared contracts
│   └── Services/
│       ├── Identity/
│       │   └── ShopSphere.Services.Identity/
│       ├── Catalog/
│       │   ├── ShopSphere.Services.Catalog.Api/
│       │   └── ShopSphere.Services.Catalog.Grpc/
│       ├── Basket/
│       │   └── ShopSphere.Services.Basket.Api/
│       ├── Inventory/
│       │   ├── ShopSphere.Services.Inventory.Domain/
│       │   ├── ShopSphere.Services.Inventory.Application/
│       │   ├── ShopSphere.Services.Inventory.Infrastructure/
│       │   └── ShopSphere.Services.Inventory.Api/
│       ├── Ordering/
│       │   ├── ShopSphere.Services.Ordering.Domain/
│       │   ├── ShopSphere.Services.Ordering.Application/
│       │   ├── ShopSphere.Services.Ordering.Infrastructure/
│       │   └── ShopSphere.Services.Ordering.Api/
│       ├── Payment/
│       │   └── ShopSphere.Services.Payment.Api/
│       ├── Notification/
│       │   └── ShopSphere.Services.Notification.Api/
│       └── Gateway/
│           └── ShopSphere.Gateway.Yarp/
├── tests/
│   ├── ShopSphere.UnitTests/
│   └── ShopSphere.IntegrationTests/
├── docker/
│   ├── docker-compose.yml
│   └── docker-compose.override.yml
└── ShopSphere.sln
```

---

## 4. Coding Conventions & Code Quality Standards

### 4.1 C# & .NET Guidelines
- Always use file-scoped namespaces (e.g., `namespace ShopSphere.Services.Ordering.Domain;`).
- Declare immutable data carriers as `public record` (commands, queries, events, DTOs).
- Avoid primitive obsession: Wrap IDs and distinct values into strongly-typed value objects or typed IDs where appropriate.
- Configure asynchronous methods properly with `CancellationToken cancellationToken = default` passed downstream to all I/O calls.
- Enforce clean separation in DDD services:
  - **Domain:** Pure business logic. Zero dependencies on ASP.NET Core, EF Core, or MediatR.
  - **Application:** Commands, Queries, Handlers (MediatR), Validators (FluentValidation).
  - **Infrastructure:** EF Core `DbContext`, Dapper repositories, external service clients.
  - **Api:** Minimal APIs / Carter modules, Swagger documentation filters, middleware.

### 4.2 Endpoint Architecture (Carter + Minimal APIs)
Endpoints must NOT use bloated MVC controllers. Use Carter modules inheriting from `ICarterModule`:

```csharp
public class ProductsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalog/products")
                       .WithTags("Products");

        group.MapGet("/", GetProducts)
             .WithName(nameof(GetProducts))
             .Produces<PagedResult<ProductResponse>>(StatusCodes.Status200OK)
             .ProducesProblem(StatusCodes.Status400BadRequest);
             
        group.MapPost("/", CreateProduct)
             .WithName(nameof(CreateProduct))
             .RequireAuthorization("AdminPolicy")
             .Produces<ProductResponse>(StatusCodes.Status201Created)
             .ProducesValidationProblem();
    }

    private static async Task<IResult> GetProducts([AsParameters] GetProductsQuery query, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(query, ct);
        return Results.Ok(result);
    }
}
```

### 4.3 Validation & Error Handling
- Never throw raw exceptions for expected domain failures.
- Pipeline validation must execute before hitting domain logic via MediatR IPipelineBehavior:
  - Validate incoming commands using `FluentValidation`.
  - On validation error, return RFC 7807 `ValidationProblem` response.
- Use a global exception handler middleware (`app.UseExceptionHandler()`) to intercept unhandled exceptions and format them into RFC 7807 JSON.

---

## 5. Event-Driven Messaging & Transactional Outbox Rules

### 5.1 Outbox Enforcement
Every service mutating domain state that must emit an integration event MUST use the MassTransit Transactional Outbox:

```csharp
services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<OrderingDbContext>(o =>
    {
        o.UseSqlServer();
        o.UseBusOutbox();
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(configuration["RabbitMQ:Host"], "/", h =>
        {
            h.Username(configuration["RabbitMQ:Username"]);
            h.Password(configuration["RabbitMQ:Password"]);
        });
        cfg.ConfigureEndpoints(context);
    });
});
```

### 5.2 Consumer Idempotency
- All MassTransit consumers must be safe against duplicate message deliveries.
- Use MassTransit Inbox state tables or business key checks before processing state changes.

---

## 6. Resilience Pipeline Standards (Polly v8)

All inter-service HTTP clients and gRPC clients must be wired with Polly v8 resilience pipelines configured in DI:

```csharp
services.AddHttpClient<CatalogClient>(client =>
{
    client.BaseAddress = new Uri(configuration["CatalogService:Url"]);
})
.AddResilienceHandler("custom-pipeline", builder =>
{
    builder.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(200),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true
    });
    builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        SamplingDuration = TimeSpan.FromSeconds(30),
        FailureRatio = 0.5,
        MinimumThroughput = 5,
        BreakDuration = TimeSpan.FromSeconds(30)
    });
    builder.AddTimeout(TimeSpan.FromSeconds(5));
});
```

---

## 7. Prohibited Agent Behaviors (Strict Antipatterns)

The agent must NEVER:
1. **Never** mix business logic inside presentation endpoints or Carter modules.
2. **Never** perform direct database joins or SQL queries across microservice boundaries.
3. **Never** generate hardcoded secrets, connection strings, or signing keys in `.cs` files or committed `appsettings.json`.
4. **Never** bypass the Outbox Pattern by directly invoking `IBus.Publish` inside an EF Core transaction block.
5. **Never** use synchronous HTTP calls where an asynchronous integration event is prescribed by the `SDD.md` and `SRS.md`.
6. **Never** write empty test fixtures or tests that pass trivially without asserting state.