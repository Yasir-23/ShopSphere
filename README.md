# ShopSphere

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0%20LTS-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C# 13](https://img.shields.io/badge/C%23-13-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![Docker Compose](https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker&logoColor=white)](https://www.docker.com/)
[![Tests](https://img.shields.io/badge/Tests-39%2F39%20Passing-brightgreen?logo=checkmarx&logoColor=white)](#automated-testing)
[![Architecture](https://img.shields.io/badge/Architecture-Clean%20%2F%20DDD%20%2F%20Microservices-blueviolet)](#architecture--system-design)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**ShopSphere** is an enterprise-grade, distributed e-commerce microservices platform built using **.NET 8 (LTS)** and **C# 13**. Designed from the ground up following **Clean Architecture**, **Domain-Driven Design (DDD)**, and **Event-Driven Architecture (EDA)** principles, ShopSphere demonstrates resilient, scalable, production-ready distributed systems engineering.

---

## Table of Contents

- [Overview](#overview)
- [Architecture & System Design](#architecture--system-design)
  - [High-Level Architecture Map](#high-level-architecture-map)
  - [Microservices Portfolio](#microservices-portfolio)
  - [Distributed Saga & Event Orchestration](#distributed-saga--event-orchestration)
  - [Observability & Distributed Tracing](#observability--distributed-tracing)
- [Prerequisites](#prerequisites)
- [Local Development Setup](#local-development-setup)
  - [1. Backing Infrastructure Setup](#1-backing-infrastructure-setup)
  - [2. Database Migrations](#2-database-migrations)
  - [3. Running the Microservices Locally](#3-running-the-microservices-locally)
- [Automated Testing](#automated-testing)
- [Production Deployment (Zero-Code Workflow)](#production-deployment-zero-code-workflow)
  - [Multi-Stage Hardened Dockerfiles](#multi-stage-hardened-dockerfiles)
  - [Single-Command Production Boot](#single-command-production-boot)
  - [Environment Variable Override Reference](#environment-variable-override-reference)
- [API Gateway Routing & Endpoints](#api-gateway-routing--endpoints)
- [Checkout Flow Walkthrough](#checkout-flow-walkthrough)
- [License](#license)

---

## Overview

ShopSphere solves real-world e-commerce scalability and reliability challenges through polyglot persistence, asynchronous messaging, compensating transactions, and distributed tracing:

- **Decoupled Autonomy**: Each microservice encapsulates its own domain, business rules, and isolated database (Polyglot persistence spanning SQL Server, PostgreSQL, MongoDB, and Redis).
- **Asynchronous Messaging**: Event-driven communication powered by **MassTransit** over **RabbitMQ**, guaranteeing at-least-once delivery, retry policies, and dead-letter queue (DLQ) isolation.
- **Dual-Phase Saga Orchestration**: MassTransit State Machine saga (`OrderStateMachine`) guarantees eventual consistency across distributed inventory reservation and payment capture without distributed 2PC locks.
- **High-Performance Gateway**: Microsoft **YARP (Yet Another Reverse Proxy)** delivers centralized traffic management, IP-based fixed-window rate limiting, and CORS enforcement.
- **Enterprise Observability**: End-to-end distributed tracing via **OpenTelemetry (OTLP)** to **Jaeger** and structured centralized logging via **Serilog** to **Seq**.

---

## Architecture & System Design

### High-Level Architecture Map

```
                             +-----------------------------------+
                             |     Clients / External Callers    |
                             +-----------------+-----------------+
                                               |
                                     HTTP/REST (Port 5001)
                                               v
            +-------------------------------------------------------------------------+
            |                        API GATEWAY (Microsoft YARP)                     |
            |      • Fixed-Window IP Rate Limiting    • Centralized CORS Policy       |
            |      • Active Health Check Probing      • Reverse Proxy Routing         |
            +-----+------------+------------+------------+------------+------------+--+
                  |            |            |            |            |            |
             /api/v1/identity  |            |            |            |            |
                  |       /api/v1/catalog   |            |            |            |
                  |            |       /api/v1/basket    |            |            |
                  |            |            |       /api/v1/inventory |            |
                  |            |            |            |       /api/v1/orders    |
                  |            |            |            |            |       /api/v1/payments
                  v            v            v            v            v            v
            +-----------+ +-----------+ +-----------+ +-----------+ +-----------+ +-----------+
            |  Identity | |  Catalog  | |  Basket   | | Inventory | |  Ordering | |  Payment  |
            |  Service  | |  Service  | |  Service  | |  Service  | |  Service  | |  Service  |
            | (Port 5005| | (Port 5010| | (Port 5020| | (Port 5030| | (Port 5040| | (Port 5050|
            +-----+-----+ +-----+-----+ +-----+-----+ +-----+-----+ +-----+-----+ +-----+-----+
                  |             ^             |             ^             ^             ^
                  |             | gRPC Client |             |             |             |
                  |             +-------------+             |             |             |
                  |              (Validate Price)           |             |             |
                  v                                         |             |             |
            +-----------+                                   |             |             |
            |PostgreSQL |                                   |             |             |
            |(Ident. DB)|                                   |             |             |
            +-----------+                                   |             |             |
                                                            v             v             v
            +-----------+ +-----------+ +-----------+  +---------------------------------------+
            |  MongoDB  | |   Redis   | |PostgreSQL |  |          RABBITMQ EVENT BUS           |
            | (Catalog) | |  (Cache/  | | (Inventory|  |        (MassTransit Broker)           |
            |           | |  Basket)  | |    DB)    |  | • OrderCreated    • ReserveInventory  |
            +-----------+ +-----------+ +-----------+  | • InventoryResv   • ProcessPayment    |
                                                       | • PaymentProc     • ReleaseInventory  |
                                                       +---------------------------------------+
                                                                          |
                                                       +------------------+--------------------+
                                                       |                                       |
                                                       v                                       v
                                          +-------------------------+             +-------------------------+
                                          |       DATALUST SEQ      |             |     JAEGER TRACING      |
                                          |   Centralized Logging   |             |  OpenTelemetry (OTLP)   |
                                          |   HTTP UI: Port 15341   |             |   HTTP UI: Port 16686   |
                                          +-------------------------+             +-------------------------+
```

---

### Microservices Portfolio

| Service | Architecture & Tech Stack | Data Store | Dev Port | Prod Port | Core Responsibilities |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **API Gateway** | Microsoft YARP, Fixed-Window Rate Limiter | *Stateless* | `5001` | `5001` | Reverse proxy routing, IP rate limiting (`100 req/min`), centralized CORS, active destination health monitoring. |
| **Identity Service** | Carter Minimal APIs, ASP.NET Core Identity | PostgreSQL (`15432`) | `5005` | `5010` | User registration, authentication, JWT issuing, refresh tokens, role-based access control (RBAC). |
| **Catalog Service** | FastEndpoints, gRPC Server (`catalog.proto`) | MongoDB 7.0 (`27018`) | `5010` | `5020` | High-throughput product catalog, categorisation, pagination, internal gRPC price & stock query interface. |
| **Basket Service** | Carter Minimal APIs, gRPC Client | Redis 7.2 (`16379`) | `5020` | `5050` | Distributed customer shopping carts with TTL expiration, checkout triggers, dynamic gRPC price validation against Catalog. |
| **Inventory Service** | Carter Minimal APIs, MassTransit Consumers | PostgreSQL (`15432`) | `5030` | `5030` | Real-time SKU stock levels, atomic reservation handling, optimistic concurrency, and compensating release workflows. |
| **Ordering Service** | Clean Architecture (Domain, App, Infra, API), MediatR CQRS | SQL Server 2022 (`11433`) | `5040` | `5040` | Order lifecycle orchestrator, MassTransit State Machine Saga (`OrderStateMachine`), EF Core persistence. |
| **Payment Service** | Carter Minimal APIs, MassTransit Consumers | PostgreSQL (`15432`) | `5050` | `5060` | Stripe mock simulator, card capture, idempotent transaction ledger, webhook handling, and payment failure handling. |

---

### Distributed Saga & Event Orchestration

ShopSphere employs a choreograph-orchestrated **Dual-Phase Checkout Saga** managed by MassTransit's `OrderStateMachine` within the Ordering Service. This coordinates distributed state changes across Inventory and Payment without locking databases.

```mermaid
stateDiagram-v2
    [*] --> Initial
    Initial --> AwaitingInventory : OrderCreatedIntegrationEvent\n(Publishes ReserveInventoryCommand)
    
    state AwaitingInventory {
        [*] --> ReservingStock
        ReservingStock --> StockReserved : Inventory Reserved Successfully
        ReservingStock --> StockFailed : Insufficient Stock Available
    }

    AwaitingInventory --> AwaitingPayment : InventoryReserved\n(Publishes ProcessPaymentCommand)
    AwaitingInventory --> Cancelled : InventoryReservationFailed\n(Cancel Order in DB)

    state AwaitingPayment {
        [*] --> ProcessingCharge
        ProcessingCharge --> ChargeSuccess : Payment Authorized & Captured
        ProcessingCharge --> ChargeFailed : Card Declined / Provider Error
    }

    AwaitingPayment --> Completed : PaymentProcessed\n(Mark Order Completed)
    AwaitingPayment --> Cancelled : PaymentFailed\n(Publishes ReleaseInventoryCommand Compensation)

    Completed --> [*]
    Cancelled --> [*]
```

#### Compensation Workflow
1. **Inventory Reservation Failure**: If any requested SKU lacks sufficient quantity, `InventoryReservationFailedIntegrationEvent` triggers the saga to immediately cancel the order and persist failure diagnostics.
2. **Payment Failure**: If payment is declined, `PaymentFailedIntegrationEvent` triggers a compensating transaction: the saga publishes `ReleaseInventoryCommand` instructing the Inventory Service to restore reserved quantities back to available stock before transitioning the order to `Cancelled`.

---

### Observability & Distributed Tracing

ShopSphere provides enterprise-grade observability using open standards:
- **Structured Logging (Serilog + Seq)**: All services enrich log events with `Application`, `Environment`, `TraceId`, and `SpanId`, emitting logs to Seq ingestion (Port `15342`) with queryable dashboards at `http://localhost:15341`.
- **Distributed Tracing (OpenTelemetry + Jaeger)**: Every HTTP request, gRPC invocation, and RabbitMQ message is instrumented with W3C TraceContext propagation, providing end-to-end trace waterfalls inside Jaeger UI at `http://localhost:16686` (OTLP receiver on port `4317`).

---

## Prerequisites

Before setting up ShopSphere, ensure your environment satisfies the following requirements:

- **[.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** (or .NET 9.0 SDK runtime)
- **[Docker Desktop](https://www.docker.com/products/docker-desktop/)** (version 24+ with Docker Compose v2)
- **Git**
- Optional: **[PowerShell 7+](https://github.com/PowerShell/PowerShell)** or **curl** / **Postman** for API testing

---

## Local Development Setup

### 1. Backing Infrastructure Setup

All backing databases, message brokers, and observability sinks are orchestrated via Docker Compose with dedicated non-standard host ports to prevent port conflicts:

```powershell
# Navigate to the repository root
cd D:\dotnet\ShopSphere

# Start backing infrastructure in detached mode
docker compose -f docker/docker-compose.yml --env-file docker/.env up -d
```

Verify that all 7 infrastructure containers are healthy:
```powershell
docker compose -f docker/docker-compose.yml ps
```

| Infrastructure | Container Name | Host Port | Internal Port | Healthcheck |
| :--- | :--- | :--- | :--- | :--- |
| **SQL Server 2022** | `shopsphere-sqlserver` | `11433` | `1433` | `sqlcmd -Q 'SELECT 1'` |
| **PostgreSQL 16** | `shopsphere-postgres` | `15432` | `5432` | `pg_isready` |
| **MongoDB 7.0** | `shopsphere-mongodb` | `27018` | `27017` | `mongosh --eval 'ping'` |
| **Redis 7.2** | `shopsphere-redis` | `16379` | `6379` | `redis-cli ping` |
| **RabbitMQ 3.13** | `shopsphere-rabbitmq` | `15672` (AMQP) / `25672` (UI) | `5672` / `15672` | `rabbitmq-diagnostics ping` |
| **Datalust Seq** | `shopsphere-seq` | `15341` (UI) / `15342` (Ingest)| `80` / `5341` | `GET /health` |
| **Jaeger All-in-One** | `shopsphere-jaeger` | `16686` (UI) / `4317` (OTLP) | `16686` / `4317` | `GET /` |

---

### 2. Database Migrations

ShopSphere services automatically verify and apply EF Core migrations and initial seeding on startup.

If you prefer applying migrations manually via the .NET CLI:

```powershell
# Identity Service (PostgreSQL)
dotnet ef database update --project src/Services/Identity/ShopSphere.Services.Identity

# Inventory Service (PostgreSQL)
dotnet ef database update --project src/Services/Inventory/ShopSphere.Services.Inventory

# Ordering Service (SQL Server 2022)
dotnet ef database update --project src/Services/Ordering/ShopSphere.Services.Ordering.Infrastructure --startup-project src/Services/Ordering/ShopSphere.Services.Ordering.Api

# Payment Service (PostgreSQL)
dotnet ef database update --project src/Services/Payment
```

---

### 3. Running the Microservices Locally

You can launch all services in separate terminals or run them concurrently via Visual Studio multiple startup projects:

```powershell
# 1. API Gateway (Port 5001)
dotnet run --project src/Gateway/ShopSphere.ApiGateway.csproj

# 2. Identity Service (Port 5005)
dotnet run --project src/Services/Identity/ShopSphere.Services.Identity/ShopSphere.Services.Identity.csproj

# 3. Catalog Service (Port 5010)
dotnet run --project src/Services/Catalog/ShopSphere.Services.Catalog.Api/ShopSphere.Services.Catalog.Api.csproj

# 4. Basket Service (Port 5020)
dotnet run --project src/Services/Basket/ShopSphere.Services.Basket/ShopSphere.Services.Basket.csproj

# 5. Inventory Service (Port 5030)
dotnet run --project src/Services/Inventory/ShopSphere.Services.Inventory/ShopSphere.Services.Inventory.csproj

# 6. Ordering Service (Port 5040)
dotnet run --project src/Services/Ordering/ShopSphere.Services.Ordering.Api/ShopSphere.Services.Ordering.Api.csproj

# 7. Payment Service (Port 5050)
dotnet run --project src/Services/Payment/ShopSphere.Services.Payment.csproj
```

Once running, navigate to:
- **API Gateway**: `http://localhost:5001`
- **Seq Centralized Logs**: `http://localhost:15341`
- **Jaeger Distributed Tracing**: `http://localhost:16686`
- **RabbitMQ Management Dashboard**: `http://localhost:25672` (Username: `shopsphere` / Password: `ShopSphere_RabbitMQ_2026!`)

---

## Automated Testing

The solution includes comprehensive unit, integration, and end-to-end tests covering all domain aggregates, state machine transitions, gRPC clients, and gateway routing.

Run the entire test suite via:

```powershell
dotnet test ShopSphere.sln
```

### Verified Test Suite Breakdown (39/39 Passing)

```
Test Run Summary:
--------------------------------------------------------------------------------------
Assembly                              Result    Passed  Failed  Skipped  Duration
--------------------------------------------------------------------------------------
ShopSphere.ApiGateway.Tests.dll       PASSED    6       0       0        ~4.0s
ShopSphere.Services.Basket.Tests.dll  PASSED    4       0       0        ~1.0s
ShopSphere.Services.Inventory.Tests   PASSED    6       0       0        ~1.0s
ShopSphere.Services.Ordering.Tests    PASSED    7       0       0        ~0.8s
ShopSphere.Services.Payment.Tests     PASSED    9       0       0        ~3.0s
ShopSphere.E2E.Tests.dll              PASSED    7       0       0        ~19.0s
--------------------------------------------------------------------------------------
TOTAL: 39 Passed, 0 Failed, 0 Skipped (100% Success Rate)
```

---

## Production Deployment (Zero-Code Workflow)

### Multi-Stage Hardened Dockerfiles

All microservices utilize production-hardened multi-stage Dockerfiles adhering to cloud-native security best practices:
- **Base Image**: Ultra-lightweight `mcr.microsoft.com/dotnet/aspnet:8.0-alpine` reducing container attack surface.
- **Least Privilege Execution**: Explicitly executed as non-root user `USER app`.
- **Deterministic Builds**: Restores dependencies against target project before publishing without build caches.

### Single-Command Production Boot

The entire ecosystem (all 7 microservices + 7 infrastructure dependencies) can be built and launched with a single command:

```powershell
# Build and run the complete production microservices stack
docker compose -f docker/docker-compose.prod.yml up -d --build
```

To stop the entire stack:
```powershell
docker compose -f docker/docker-compose.prod.yml down
```

---

### Environment Variable Override Reference

For containerized cloud deployments (Kubernetes, AWS ECS, Azure Container Apps, Nomad), configure the following environment variable overrides:

| Variable Category | Environment Variable | Default Value / Template |
| :--- | :--- | :--- |
| **ASP.NET Core** | `ASPNETCORE_ENVIRONMENT` | `Production` |
| **JWT Authentication** | `Jwt__SecretKey` | `${JWT_SECRET}` (256-bit secret string) |
| | `Jwt__Issuer` | `ShopSphere` |
| | `Jwt__Audience` | `ShopSphere.Clients` |
| **SQL Server** | `ConnectionStrings__OrderingDb` | `Server=sqlserver,1433;Database=ShopSphere_Ordering;User Id=sa;Password=...;TrustServerCertificate=True;` |
| | `ConnectionStrings__IdentityDb` | `Server=sqlserver,1433;Database=ShopSphere_Identity;User Id=sa;Password=...;TrustServerCertificate=True;` |
| **PostgreSQL** | `ConnectionStrings__InventoryDb`| `Host=postgres;Port=5432;Database=shopsphere_inventory;Username=...;Password=...;` |
| | `ConnectionStrings__PaymentDb`  | `Host=postgres;Port=5432;Database=shopsphere_payment;Username=...;Password=...;` |
| **MongoDB** | `MongoDb__ConnectionString`     | `mongodb://mongoadmin:password@mongodb:27017/ShopSphere_Catalog?authSource=admin` |
| | `MongoDb__DatabaseName`         | `ShopSphere_Catalog` |
| **Redis** | `Redis__ConnectionString`       | `redis:6379` |
| **RabbitMQ** | `RabbitMQ__Host`                | `rabbitmq` |
| | `RabbitMQ__Port`                | `5672` |
| | `RabbitMQ__Username`            | `shopsphere` |
| | `RabbitMQ__Password`            | `${RABBITMQ_DEFAULT_PASS}` |
| **Observability** | `Seq__ServerUrl`                | `http://seq:80` |
| | `OpenTelemetry__OtlpEndpoint`   | `http://jaeger:4317` |

---

## API Gateway Routing & Endpoints

All external traffic enters through the API Gateway on port `5001`. The gateway enforces fixed-window rate limiting (`100 requests / 60 seconds per IP`) and routes requests to downstream clusters:

| Route Prefix | Destination Cluster | Target Endpoint Example |
| :--- | :--- | :--- |
| `/api/v1/identity/*` | `identity-cluster` | `POST /api/v1/identity/users/register`, `POST /api/v1/identity/users/login` |
| `/api/v1/catalog/*` | `catalog-cluster` | `GET /api/v1/catalog/products`, `POST /api/v1/catalog/products` |
| `/api/v1/basket/*` | `basket-cluster` | `GET /api/v1/basket/{customerId}`, `POST /api/v1/basket/checkout` |
| `/api/v1/inventory/*`| `inventory-cluster` | `GET /api/v1/inventory/{productId}`, `POST /api/v1/inventory/reserve` |
| `/api/v1/orders/*` | `ordering-cluster` | `GET /api/v1/orders/{orderId}`, `GET /api/v1/orders/customer/{customerId}` |
| `/api/v1/payments/*` | `payment-cluster` | `POST /api/v1/payments/process`, `POST /api/v1/payments/webhook` |

---

## Checkout Flow Walkthrough

A complete customer purchase executes through the following distributed stages:

1. **Authentication**: Client authenticates with `Identity Service` (`POST /api/v1/identity/users/login`) receiving a JWT Bearer token.
2. **Catalog Browsing**: Client queries `Catalog Service` (`GET /api/v1/catalog/products`).
3. **Cart Assembly**: Client adds items to cart via `Basket Service` (`POST /api/v1/basket`). Basket synchronizes unit prices dynamically against Catalog via internal gRPC.
4. **Checkout Trigger**: Client triggers checkout (`POST /api/v1/basket/checkout`). The Basket Service publishes `OrderCreatedIntegrationEvent` and clears the Redis cache.
5. **Saga Activation**: `Ordering Service` consumes `OrderCreatedIntegrationEvent`, creates the order entity in SQL Server (`Pending`), and starts `OrderStateMachine`.
6. **Stock Reservation**: The State Machine publishes `ReserveInventoryCommand`. `Inventory Service` attempts atomic decrement of available stock.
   - *If successful*: Publishes `InventoryReservedIntegrationEvent`.
   - *If failed*: Publishes `InventoryReservationFailedIntegrationEvent`, leading to order cancellation.
7. **Payment Capture**: Upon receiving `InventoryReservedIntegrationEvent`, the State Machine transitions to `AwaitingPayment` and issues `ProcessPaymentCommand`.
   - *If payment succeeds*: `PaymentService` captures funds and publishes `PaymentProcessedIntegrationEvent`. The Saga completes the order (`Completed`).
   - *If payment fails*: `PaymentService` publishes `PaymentFailedIntegrationEvent`. The Saga publishes a compensating `ReleaseInventoryCommand` to revert the stock back to Inventory and transitions order to `Cancelled`.

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
