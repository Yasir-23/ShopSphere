# System Design Document (SDD)

## Document Details
- **Project Name:** ShopSphere E-Commerce Microservices Platform
- **Document Version:** 1.0.0
- **Status:** Approved / Ready for Implementation
- **Target Platform:** .NET 9 / C# 13 Distributed Microservices Architecture

---

## 1. Architectural Philosophy & Design Principles

ShopSphere is engineered as an event-driven, distributed microservices platform adhering strictly to cloud-native architectural patterns:

- **Database-per-Service:** Complete schema isolation. Services own their persistence engines (SQL Server, PostgreSQL, MongoDB, Redis) and communicate solely via asynchronous message passing (MassTransit over RabbitMQ) or synchronous typed RPCs (gRPC).
- **Clean Architecture & Domain-Driven Design (DDD):** Applied rigorously to complex transactional domains (`Order Service`, `Inventory Service`). Enforces clear separation between Domain, Application, Infrastructure, and Presentation layers.
- **CQRS (Command Query Responsibility Segregation):** Domain mutations route through MediatR commands and transactional Entity Framework Core DbContexts; high-throughput read projections route through optimized Dapper queries.
- **Outbox & Inbox Patterns:** Guarantees atomic dual-write consistency and idempotent event processing across distributed boundaries without distributed 2-phase commit (2PC) locks.
- **Fault-Tolerant Resilience:** Inter-service synchronous dependencies are isolated using Polly v8 resilience pipelines (Retry with jitter, Circuit Breaker, Timeout, and Bulkhead).

---

## 2. Global Repository & Solution Layout

ShopSphere uses a modular repository structure hosting independent deployable services, shared core packages, and container orchestrations.

```
ShopSphere/
├── docs/
│   ├── PRD.md
│   ├── SRS.md
│   ├── SDD.md
│   ├── API_SPEC.md
│   └── AGENTS.md
├── src/
│   ├── BuildingBlocks/
│   │   ├── ShopSphere.Common.Logging/
│   │   ├── ShopSphere.Common.Security/
│   │   ├── ShopSphere.Common.Resilience/
│   │   └── ShopSphere.EventBus/
│   ├── Services/
│   │   ├── Identity/
│   │   │   └── ShopSphere.Services.Identity/
│   │   ├── Catalog/
│   │   │   ├── ShopSphere.Services.Catalog.Api/
│   │   │   └── ShopSphere.Services.Catalog.Grpc/
│   │   ├── Basket/
│   │   │   └── ShopSphere.Services.Basket.Api/
│   │   ├── Inventory/
│   │   │   ├── ShopSphere.Services.Inventory.Domain/
│   │   │   ├── ShopSphere.Services.Inventory.Application/
│   │   │   ├── ShopSphere.Services.Inventory.Infrastructure/
│   │   │   └── ShopSphere.Services.Inventory.Api/
│   │   ├── Ordering/
│   │   │   ├── ShopSphere.Services.Ordering.Domain/
│   │   │   ├── ShopSphere.Services.Ordering.Application/
│   │   │   ├── ShopSphere.Services.Ordering.Infrastructure/
│   │   │   └── ShopSphere.Services.Ordering.Api/
│   │   ├── Payment/
│   │   │   └── ShopSphere.Services.Payment.Api/
│   │   └── Notification/
│   │   │   └── ShopSphere.Services.Notification.Api/
│   │   └── Gateway/
│   │       └── ShopSphere.Gateway.Yarp/
├── tests/
│   ├── ShopSphere.ArchitectureTests/
│   ├── ShopSphere.UnitTests/
│   └── ShopSphere.IntegrationTests/
├── docker/
│   ├── docker-compose.yml
│   ├── docker-compose.override.yml
│   └── prometheus/
├── ShopSphere.sln
└── README.md
```

---

## 3. Shared Building Blocks (`src/BuildingBlocks/`)

### 3.1 `ShopSphere.Common.Logging`
- **Serilog Configuration:** Standardized JSON structured output enriched with `ApplicationName`, `Environment`, `TraceId`, `SpanId`, `MachineName`, and `UserId`.
- **OpenTelemetry Instrumentation:** Automatic traces exported via OTLP gRPC endpoint (`http://jaeger:4317`). Hooks into ASP.NET Core, HttpClient, EF Core, MassTransit, and StackExchange.Redis.

### 3.2 `ShopSphere.Common.Resilience`
Polly v8 Resilience Strategy Pipelines registered via Dependency Injection:
- **Default HTTP Pipeline:**
  - Rate Limiter: Max 50 concurrent requests.
  - Timeout: 5 seconds.
  - Circuit Breaker: Breaks after 5 consecutive failures for 30 seconds; half-open state testing 3 requests.
  - Exponential Backoff Retry: 3 retries (base 100 ms with exponential backoff and ±50 ms jitter) handling HTTP `5xx`, `408`, and network connection drops.

### 3.3 `ShopSphere.EventBus`
- Encapsulates MassTransit bus registrations.
- Configures RabbitMQ host, virtual hosts, retry middleware, dead-letter exchanges (`*-error`), and serialization settings (System.Text.Json with snake_case naming).
- Houses shared Integration Event contracts and MassTransit Outbox/Inbox Entity Framework Core configurations.

---

## 4. Deep-Dive Service Specifications

### 4.1 Gateway (`ShopSphere.Gateway.Yarp`)
- **Technology:** ASP.NET Core 9 Minimal API + YARP (Yet Another Reverse Proxy).
- **Responsibilities:** Ingress traffic routing, centralized rate limiting, TLS termination, CORS enforcement, and authorization pass-through.
- **Rate Limiting:** Built-in fixed-window and token-bucket limiters:
  - Global IP Limit: 100 requests per minute per client IP.
  - Identity Route Limit: 10 requests per minute for `/api/v1/identity/login` and `/register`.
- **YARP Routing Matrix:**
  - `/api/v1/identity/**` -> `http://identity-service:8080`
  - `/api/v1/catalog/**` -> `http://catalog-service:8080`
  - `/api/v1/basket/**` -> `http://basket-service:8080`
  - `/api/v1/orders/**` -> `http://order-service:8080`
  - `/api/v1/inventory/**` -> `http://inventory-service:8080`
  - `/hubs/**` -> `http://notification-service:8080` (WebSocket / SignalR bypass)

---

### 4.2 Identity Service (`ShopSphere.Services.Identity`)
- **Persistence:** Microsoft SQL Server via EF Core 9.
- **Pattern:** ASP.NET Core Identity Core + Minimal APIs with Carter.
- **Authentication:** JWT Access Token (HMAC-SHA256, 15 min TTL) + Refresh Token stored in SQL Server (7 days sliding TTL, SHA-512 hashed).
- **Key Tables:**
  - `Users` (`Id`, `Email`, `PasswordHash`, `FirstName`, `LastName`, `CreatedAt`)
  - `Roles` (`Id`, `Name`)
  - `UserRoles` (`UserId`, `RoleId`)
  - `RefreshTokens` (`Id`, `UserId`, `TokenHash`, `ExpiresAtUtc`, `IsRevoked`, `ReplacedByTokenHash`)

---

### 4.3 Catalog Service (`ShopSphere.Services.Catalog`)
- **Persistence:** MongoDB (v7.0) via Official C# Mongo Driver.
- **Pattern:** CQRS with MediatR + Output Caching (ASP.NET Core Redis Output Cache).
- **gRPC Contract (`catalog.proto`):**
```protobuf
syntax = "proto3";
package catalog;
option csharp_namespace = "ShopSphere.Services.Catalog.Grpc";

service CatalogPriceChecker {
  rpc GetItemPrices (GetItemPricesRequest) returns (GetItemPricesResponse);
}

message GetItemPricesRequest {
  repeated string product_ids = 1;
}

message ProductPriceItem {
  string product_id = 1;
  double price = 2;
  string currency = 3;
  bool is_available = 4;
}

message GetItemPricesResponse {
  repeated ProductPriceItem items = 1;
}
```
- **MongoDB Collection `Products`:**
  - Indices: Text index on `{ Name: "text", Description: "text" }`, Compound index on `{ Category: 1, Price: 1 }`.

---

### 4.4 Basket Service (`ShopSphere.Services.Basket`)
- **Persistence:** Redis (StackExchange.Redis).
- **TTL Strategy:** Sliding expiration of 30 days (`SET key value EX 2592000`).
- **Data Model:** Stored as JSON string under key `basket:{customerId}`:
```json
{
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "items": [
    {
      "productId": "651a2b3c4d5e6f7a8b9c0d1e",
      "productName": "Ergonomic Mechanical Keyboard",
      "unitPrice": 129.99,
      "quantity": 2
    }
  ],
  "totalPrice": 259.98
}
```
- **Inter-Service Validation:** Prior to checkout, invokes `CatalogPriceChecker.GetItemPrices` over gRPC with Polly timeout (2 seconds). If prices differ, basket updates automatically and alerts customer.

---

### 4.5 Inventory Service (`ShopSphere.Services.Inventory`)
- **Persistence:** PostgreSQL 16 via EF Core (writes) and Dapper (reads).
- **Pattern:** Domain-Driven Design (DDD) with Aggregate Root.
- **Aggregate: `InventoryItem`**:
  - Properties: `Id` (Guid), `Sku` (string), `AvailableQuantity` (int), `ReservedQuantity` (int), `Version` (uint - Concurrency token).
  - Business Methods:
    - `ReserveStock(int quantity, Guid orderId)` -> Validates `AvailableQuantity >= quantity`; decrements available, increments reserved; emits `StockReservedDomainEvent`.
    - `ReleaseStock(int quantity, Guid orderId)` -> Decrements reserved, increments available; emits `StockReleasedDomainEvent`.
    - `CommitStock(int quantity)` -> Decrements reserved permanently; emits `StockDeductedDomainEvent`.
- **Concurrency Control:** PostgreSQL row-level locking via `SELECT ... FOR UPDATE` or EF Core xmin optimistic concurrency token.

---

### 4.6 Ordering Service (`ShopSphere.Services.Ordering`)
- **Persistence:** SQL Server 2022 via EF Core & Dapper.
- **Pattern:** Full DDD + MassTransit State Machine Saga (Choreographed Orchestrator).
- **Aggregate: `Order`**:
  - `OrderId` (Guid)
  - `CustomerId` (Guid)
  - `OrderDate` (DateTimeOffset)
  - `Status` (OrderStatus enum: `Submitted`, `AwaitingStock`, `StockReserved`, `Paid`, `Confirmed`, `Shipped`, `Cancelled`)
  - `OrderItems` (List<OrderItem> Value Object)
  - `ShippingAddress` (Value Object)
  - `TotalPrice` (decimal)

#### 4.6.1 Order Saga State Machine (MassTransit)

```
                 +-----------------------+
                 |    Initial State      |
                 +-----------+-----------+
                             |
                             | Event: OrderCreatedEvent
                             v
                 +-----------------------+
                 |   AwaitingStock       |
                 +-----------+-----------+
                             |
             +---------------+---------------+
             |                               |
             | StockReservedEvent            | StockReservationFailedEvent
             v                               v
+------------------------+      +------------------------+
|     StockReserved      |      |     Faulted/Failed     |
+-----------+------------+      +------------------------+
            |                   | Action: Set Order =    |
            | Action: Publish   |         Cancelled      |
            | ProcessPayment    +------------------------+
            v
+------------------------+
|    AwaitingPayment     |
+-----------+------------+
            |
    +-------+-------+
    |               |
    | PaidEvent     | PaymentFailedEvent
    v               v
+-------+       +------------------------+
| Conf. |       |    Compensating State  |
+-------+       +------------------------+
                | Action: Publish        |
                | ReleaseStockCommand;   |
                | Set Order = Cancelled  |
                +------------------------+
```

---

### 4.7 Payment Service (`ShopSphere.Services.Payment`)
- **Persistence:** PostgreSQL 16 via EF Core.
- **Role:** Asynchronous transaction simulator.
- **Processing Logic:**
  - Receives `ProcessPaymentCommand` via MassTransit.
  - Simulates latency (500 ms - 1500 ms).
  - Deterministic evaluation:
    - Card number ending in `0000` -> Hard failure (`PaymentFailedEvent`, Reason: "Card Declined").
    - Card number ending in `9999` -> Timeout simulation / Transient failure.
    - Any other card -> Success (`PaymentCompletedEvent`, TransactionId generated).

---

### 4.8 Notification Service (`ShopSphere.Services.Notification`)
- **Role:** Real-time push engine & simulated transactional communications.
- **Hubs:**
  - `/hubs/orders`: Authenticated endpoint mapping `Context.UserIdentifier` to push order status lifecycle updates to consumers.
  - `/hubs/admin`: Streams real-time platform metrics (Orders/min, gross sales, failure rate) to Admin UI.

---

## 5. Transactional Outbox & Inbox Architecture

To avoid dual-write hazards and guarantee at-least-once message delivery without distributed transactions, all mutating services use the MassTransit EF Core Outbox/Inbox integration.

```
+-------------------------------------------------------------------------+
|                  Service Boundary (Order or Inventory)                  |
|                                                                         |
|  1. Incoming Request                                                    |
|         │                                                               |
|         ▼                                                               |
|  ┌──────────────┐     2. Begin Transaction                              |
|  │  DbContext   │ ───────────────────────────────────┐                  |
|  └──────────────┘                                    │                  |
|         │                                            ▼                  |
|         ├─ Write Domain Changes (e.g., Orders)  ┌─────────┐             |
|         └─ Write Outbox Message Record          │ Database│ (SQL/Postgres)
|                                                 └─────────┘             |
|  3. Commit Transaction ──────────────────────────────▲                  |
|                                                                         |
|  4. MassTransit Outbox Publisher Service                                |
|         │ Reads uncommitted Outbox messages                             |
|         ▼                                                               |
|  ┌──────────────┐                                                       |
|  │RabbitMQ Bus  │ ──► Message published with DeliveryMode=Persistent    |
|  └──────────────┘                                                       |
+-------------------------------------------------------------------------+
```

### Outbox Pattern Guarantees
- Domain state mutations and the pending message envelope are saved within the same local database transaction.
- If the database transaction fails, neither the entity nor the message is persisted.
- If RabbitMQ is down, the message remains safely in the database outbox table. A background publisher process polls and pushes pending records as soon as broker connectivity resumes.

---

## 6. Distributed Tracing & Observability Schema

OpenTelemetry integrates at every layer:
- **W3C Standard Context:** Gateway generates a W3C `traceparent` (e.g., `00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01`).
- **HTTP Header Injection:** YARP propagates `traceparent` and `tracestate` headers to downstream REST APIs.
- **gRPC Metadata:** Basket-to-Catalog client pushes activity baggage over gRPC metadata.
- **MassTransit Envelope:** MassTransit automatically embeds parent Span and Trace IDs into RabbitMQ message headers. Consumers extract context, producing continuous multi-service distributed traces in Jaeger.

---

## 7. Database Schemas (DDL / Initial Tables)

### 7.1 SQL Server (`ShopSphere.Services.Ordering`)
```sql
CREATE TABLE Orders (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    CustomerId UNIQUEIDENTIFIER NOT NULL,
    OrderDate DATETIMEOFFSET NOT NULL,
    Status INT NOT NULL,
    TotalAmount DECIMAL(18, 2) NOT NULL,
    ShippingAddress_Street NVARCHAR(200) NOT NULL,
    ShippingAddress_City NVARCHAR(100) NOT NULL,
    ShippingAddress_State NVARCHAR(100) NOT NULL,
    ShippingAddress_ZipCode NVARCHAR(20) NOT NULL,
    ShippingAddress_Country NVARCHAR(100) NOT NULL,
    RowVersion ROWVERSION
);

CREATE TABLE OrderItems (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    OrderId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES Orders(Id) ON DELETE CASCADE,
    ProductId NVARCHAR(50) NOT NULL,
    ProductName NVARCHAR(250) NOT NULL,
    UnitPrice DECIMAL(18, 2) NOT NULL,
    Quantity INT NOT NULL
);

CREATE INDEX IX_Orders_CustomerId ON Orders(CustomerId);
CREATE INDEX IX_Orders_OrderDate ON Orders(OrderDate DESC);
```

### 7.2 PostgreSQL (`ShopSphere.Services.Inventory`)
```sql
CREATE TABLE inventory_items (
    id UUID PRIMARY KEY,
    sku VARCHAR(64) NOT NULL UNIQUE,
    available_quantity INT NOT NULL CHECK (available_quantity >= 0),
    reserved_quantity INT NOT NULL DEFAULT 0 CHECK (reserved_quantity >= 0),
    xmin XID NOT NULL -- Optimistic concurrency token
);

CREATE TABLE inventory_reservations (
    id UUID PRIMARY KEY,
    inventory_item_id UUID NOT NULL REFERENCES inventory_items(id),
    order_id UUID NOT NULL,
    quantity INT NOT NULL,
    reserved_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX ix_inventory_sku ON inventory_items(sku);
CREATE INDEX ix_inventory_reservations_order_id ON inventory_reservations(order_id);
```