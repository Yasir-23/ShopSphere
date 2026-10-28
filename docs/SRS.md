# Software Requirements Specification (SRS)

## Document Details
- **Project Name:** ShopSphere E-Commerce Microservices Platform
- **Document Version:** 1.0.0
- **Status:** Approved / Engineering Baseline
- **Target Platform:** .NET 9 / C# 13 Distributed Microservices Architecture

---

## 1. Introduction & System Scope

### 1.1 Purpose
This document specifies the software requirements for the **ShopSphere E-Commerce Microservices Platform**. It establishes the functional, architectural, data, interface, and reliability contracts required for engineering teams and autonomous coding agents to build, verify, and deploy the platform without architectural divergence.

### 1.2 System Scope
ShopSphere decomposes traditional monolithic retail workflows into autonomous microservices. The boundary includes:
- **API Gateway (YARP):** Ingress proxying, rate limiting, routing, and centralized JWT validation.
- **Identity Service:** User identity, cryptographic credentials, JWT issuing, and refresh token rotation.
- **Catalog Service:** Product metadata, categories, full-text search, and gRPC price verification.
- **Basket Service:** Redis-backed temporary shopping carts with sliding expiration and checkout initiation.
- **Inventory Service:** Stock ledger, atomic allocations, reservations, and compensating releases.
- **Order Service:** Order lifecycle state machine, immutable price snapshots, and MassTransit Saga orchestration.
- **Payment Service:** Idempotent payment processing simulation, transaction authorization, and refund compensations.
- **Notification Service:** Real-time push updates via SignalR and asynchronous email/SMS notifications.

### 1.3 Definitions, Acronyms, and Abbreviations
- **Saga:** A design pattern to manage distributed transactions via a sequence of local transactions coordinated through integration events.
- **Compensating Transaction:** An operation that reverses the business effect of a previously committed local transaction.
- **Outbox Pattern:** A pattern guaranteeing that database mutations and outgoing integration messages are committed in the same local ACID transaction.
- **Inbox Pattern:** A pattern ensuring that incoming integration messages are processed idempotently, discarding duplicates.
- **TTL:** Time-To-Live expiration window.
- **RBAC:** Role-Based Access Control.

### 1.4 Architectural Compliance & Constraints
1. **Language & Runtime:** .NET 9 (C# 13) with nullable reference types enabled (`<Nullable>enable</Nullable>`) and treat warnings as errors (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`).
2. **API Styles:** ASP.NET Core Minimal APIs structured using Carter modules.
3. **Data Isolation:** Strictly one datastore per microservice. Cross-database queries or shared connection strings are prohibited.
4. **Communication:**
   - Synchronous Internal: gRPC (HTTP/2 with Protobuf).
   - Synchronous Ingress: HTTPS REST (JSON).
   - Asynchronous Cross-Boundary: MassTransit over RabbitMQ.

---

## 2. System Boundaries & Autonomous Services

### 2.1 Identity Service (`ShopSphere.Services.Identity`)
- **SRS-ID-01 (Registration):** Must expose `POST /api/v1/identity/register` accepting `email`, `password`, `firstName`, and `lastName`. Passwords must be hashed using PBKDF2 with HMAC-SHA256 (minimum 100,000 iterations) or Argon2id.
- **SRS-ID-02 (Authentication):** Must expose `POST /api/v1/identity/login`. Returns a 15-minute validity JWT Access Token and an opaque 7-day Refresh Token.
- **SRS-ID-03 (Token Refresh & Rotation):** Must expose `POST /api/v1/identity/refresh`. When a refresh token is exchanged, the existing token must be revoked and replaced with a newly issued token pair. If a revoked token is reused, all active tokens for that user session family must be revoked immediately.
- **SRS-ID-04 (Claims Profile):** The JWT must carry standard claims: `sub` (User GUID), `email`, `role` (`Customer`, `Admin`), and `jti` (Token ID).

### 2.2 Catalog Service (`ShopSphere.Services.Catalog`)
- **SRS-CAT-01 (Product Entity):** MongoDB document schema storing `Id`, `Name`, `Slug`, `Description`, `Price` (decimal), `Currency` (default "USD"), `CategoryId`, `Attributes` (key-value dictionary), and `IsActive` (bool).
- **SRS-CAT-02 (Admin Mutations):** Endpoints `POST /api/v1/catalog/products`, `PUT /api/v1/catalog/products/{id}`, and `DELETE /api/v1/catalog/products/{id}` must require `Admin` role claims.
- **SRS-CAT-03 (Public Querying):** `GET /api/v1/catalog/products` must support pagination (`pageNumber`, `pageSize`), keyword search (`searchTerm`), category filtering (`categoryId`), price bounds (`minPrice`, `maxPrice`), and sorting (`price_asc`, `price_desc`, `name_asc`, `newest`).
- **SRS-CAT-04 (gRPC Price Contract):** Exposes `CatalogPriceChecker.GetItemPrices` over gRPC to return current prices and availability for a list of product IDs.

### 2.3 Basket Service (`ShopSphere.Services.Basket`)
- **SRS-BSK-01 (Cart Lifecycle):** Persists cart state in Redis keyed by `basket:{customerId}` with a rolling 30-day TTL refreshed on every mutation.
- **SRS-BSK-02 (Cart Mutations):**
  - `GET /api/v1/basket`: Retrieves cart for the authenticated user.
  - `POST /api/v1/basket`: Upserts item list (`productId`, `productName`, `unitPrice`, `quantity`).
  - `DELETE /api/v1/basket`: Flushes the customer's cart.
- **SRS-BSK-03 (Price Validation Check):** Prior to finalizing checkout, invokes `Catalog.GetItemPrices` via gRPC. If any price deviation is detected, the cart is refreshed and a `409 Conflict` containing the updated price snapshot is returned to the client.
- **SRS-BSK-04 (Checkout Dispatch):** `POST /api/v1/basket/checkout` triggers the checkout workflow by publishing `CheckoutBasketCommand` to the broker and evacuating the Redis basket.

### 2.4 Inventory Service (`ShopSphere.Services.Inventory`)
- **SRS-INV-01 (Stock Ledger):** PostgreSQL datastore tracking `ProductId`, `Sku`, `AvailableQuantity`, and `ReservedQuantity`.
- **SRS-INV-02 (Atomic Stock Reservation):** Upon receiving `OrderStartedEvent`, verifies that `AvailableQuantity >= RequestedQuantity`. If valid, decrements `AvailableQuantity`, increments `ReservedQuantity`, and emits `StockReservedEvent`. If insufficient, emits `StockReservationFailedEvent`.
- **SRS-INV-03 (Compensating Release):** Upon receiving `ReleaseStockCommand`, decrements `ReservedQuantity` and increments `AvailableQuantity` by the specified quantity, emitting `StockReleasedEvent`.
- **SRS-INV-04 (Stock Commitment):** Upon receiving `OrderCompletedEvent`, permanently removes reserved stock from `ReservedQuantity`.

### 2.5 Order Service (`ShopSphere.Services.Ordering`)
- **SRS-ORD-01 (Order Aggregate):** SQL Server datastore persisting `OrderId`, `CustomerId`, `OrderDate`, `Status`, `TotalAmount`, `ShippingAddress`, and `OrderItems` snapshot.
- **SRS-ORD-02 (Immutable Price Snapshot):** Line items must freeze product names, SKUs, and unit prices at the time of submission; subsequent catalog edits must never mutate historical orders.
- **SRS-ORD-03 (Saga Orchestration):** Coordinates the checkout process using a MassTransit State Machine Saga:
  `Submitted` $\rightarrow$ `AwaitingStock` $\rightarrow$ `StockReserved` $\rightarrow$ `AwaitingPayment` $\rightarrow$ `Confirmed`.
- **SRS-ORD-04 (Customer Query Isolation):** Customers can only query their own orders (`CustomerId` matching JWT `sub`).
- **SRS-ORD-05 (Self-Service Cancellation):** Customers may cancel orders only prior to reaching `Shipped` status. Cancellation triggers full stock release and refund compensations.

### 2.6 Payment Service (`ShopSphere.Services.Payment`)
- **SRS-PAY-01 (Payment Simulation):** Consumes `ProcessPaymentCommand` via MassTransit. Evaluates payment deterministically:
  - Card ending in `0000`: Emits `PaymentFailedEvent` (Card Declined).
  - Card ending in `9999`: Simulates network timeout.
  - Any other valid card: Generates `TransactionId` and emits `PaymentCompletedEvent`.
- **SRS-PAY-02 (Refund Simulation):** Consumes `RefundPaymentCommand` to simulate transaction reversals when orders are cancelled post-payment.

### 2.7 Notification Service (`ShopSphere.Services.Notification`)
- **SRS-NOT-01 (Real-Time Customer Push):** Maintains SignalR hub at `/hubs/orders`. Pushes live `OrderStatusChanged` payloads to authenticated customers based on `UserIdentifier`.
- **SRS-NOT-02 (Admin Metrics Stream):** Maintains SignalR hub at `/hubs/admin`. Streams platform metrics (`ordersPerMinute`, `activeCheckouts`, `grossRevenueToday`) to authorized administrators.
- **SRS-NOT-03 (Simulated Messaging):** Consumes `OrderCompletedEvent` and `PaymentFailedEvent` to generate asynchronous transactional email and SMS logs.

### 2.8 API Gateway (`ShopSphere.Gateway.Yarp`)
- **SRS-GW-01 (Reverse Proxy Routing):** Routes external traffic to backend services based on defined route paths.
- **SRS-GW-02 (Edge Token Verification):** Validates inbound JWT headers and passes verified identity headers (`X-User-Id`, `X-User-Roles`) to internal services.
- **SRS-GW-03 (Rate Limiting):** Applies fixed-window limits (100 req/min globally per IP; 10 req/min for auth routes).

---

## 3. Detailed Functional Workflows & Distributed Transactions

### 3.1 End-to-End Checkout Saga Flow (State Machine)
1. **Initiation:** Customer posts cart checkout request $\rightarrow$ Basket Service validates prices over gRPC $\rightarrow$ Order Service creates order in `Submitted` state $\rightarrow$ Order Service publishes `OrderStartedEvent`.
2. **Stock Reservation:** Inventory Service processes `OrderStartedEvent` $\rightarrow$ Reserves stock $\rightarrow$ Publishes `StockReservedEvent`.
3. **Payment Execution:** Order Saga consumes `StockReservedEvent` $\rightarrow$ Transitions state to `AwaitingPayment` $\rightarrow$ Emits `ProcessPaymentCommand` to Payment Service.
4. **Order Confirmation:** Payment Service processes charge $\rightarrow$ Emits `PaymentCompletedEvent` $\rightarrow$ Order Saga transitions state to `Confirmed` $\rightarrow$ Publishes `OrderCompletedEvent` $\rightarrow$ Notification Service pushes SignalR alert to client.

### 3.2 Rollback & Compensation Logic
- **Inventory Failure Branch:** If Inventory Service has insufficient stock $\rightarrow$ Emits `StockReservationFailedEvent` $\rightarrow$ Order Saga sets state to `Cancelled` with reason `OutOfStock` $\rightarrow$ Client notified.
- **Payment Failure Branch:** If Payment Service declines card $\rightarrow$ Emits `PaymentFailedEvent` $\rightarrow$ Order Saga transitions to `Compensating` $\rightarrow$ Publishes `ReleaseStockCommand` to Inventory Service $\rightarrow$ Inventory Service restores stock $\rightarrow$ Order Saga sets state to `PaymentFailed` $\rightarrow$ Client notified.

### 3.3 Synchronous Price Check Flow (gRPC Basket to Catalog)
1. User requests checkout on Basket Service.
2. Basket Service extracts unique `productId` collection from cart.
3. Basket Service opens gRPC channel to Catalog Service: `CatalogPriceChecker.GetItemPrices(BatchRequest)`.
4. If catalog prices match cart snapshot: Proceed to checkout.
5. If catalog prices differ: Basket Service overrides internal Redis cart prices with live prices, halts checkout, and returns `409 Conflict` to user displaying price adjustments.

### 3.4 Outbox Pattern & Event Publishing Lifecycle
1. Application begins database transaction.
2. Application writes domain entity state changes.
3. MassTransit EF Core Outbox intercepts outgoing events and writes corresponding records to the service's `OutboxMessages` table within the same transaction.
4. Transaction commits.
5. MassTransit Outbox Publisher worker picks up uncommitted outbox rows and transmits them to RabbitMQ.
6. Upon receipt acknowledgment from RabbitMQ, outbox records are marked dispatched or removed.

---

## 4. Data Models & Persistence Specifications

### 4.1 Schema Definitions & Entities per Service

#### Identity Service (SQL Server)
- `Users`: `Id` (GUID, PK), `Email` (NVARCHAR(256), Unique), `PasswordHash` (NVARCHAR(MAX)), `FirstName` (NVARCHAR(100)), `LastName` (NVARCHAR(100)), `CreatedAt` (DATETIMEOFFSET).
- `RefreshTokens`: `Id` (GUID, PK), `UserId` (GUID, FK), `TokenHash` (NVARCHAR(MAX)), `ExpiresAt` (DATETIMEOFFSET), `IsRevoked` (BIT), `ReplacedByTokenHash` (NVARCHAR(MAX)).

#### Catalog Service (MongoDB)
- `Products`: `_id` (ObjectId/String), `name` (String), `slug` (String, Unique Index), `description` (String), `price` (Decimal128), `currency` (String), `categoryId` (String), `attributes` (BsonDocument), `isActive` (Boolean).

#### Basket Service (Redis)
- Key format: `basket:{customerId}` $\rightarrow$ JSON payload containing customer identifier, item array (SKU, name, unitPrice, quantity), and cached total calculation. TTL: 2,592,000 seconds (30 days).

#### Inventory Service (PostgreSQL)
- `inventory_items`: `id` (UUID, PK), `sku` (VARCHAR(64), Unique), `available_quantity` (INT), `reserved_quantity` (INT), `xmin` (XID, Concurrency Token).
- `inventory_reservations`: `id` (UUID, PK), `inventory_item_id` (UUID, FK), `order_id` (UUID), `quantity` (INT), `reserved_at` (TIMESTAMPTZ).

#### Ordering Service (SQL Server)
- `Orders`: `Id` (GUID, PK), `CustomerId` (GUID, Index), `OrderDate` (DATETIMEOFFSET), `Status` (INT), `TotalAmount` (DECIMAL(18,2)), `ShippingAddress_Street` (NVARCHAR(200)), `ShippingAddress_City` (NVARCHAR(100)), `ShippingAddress_State` (NVARCHAR(100)), `ShippingAddress_ZipCode` (NVARCHAR(20)), `ShippingAddress_Country` (NVARCHAR(100)), `RowVersion` (ROWVERSION).
- `OrderItems`: `Id` (GUID, PK), `OrderId` (GUID, FK), `ProductId` (NVARCHAR(50)), `ProductName` (NVARCHAR(250)), `UnitPrice` (DECIMAL(18,2)), `Quantity` (INT).

### 4.2 Data Integrity & Foreign Key Rules across Microservices
- No foreign key constraints exist across service database boundaries.
- Entity references across services must use invariant scalar identifiers (`Guid` or `String SKU`).
- Referential integrity across boundaries is enforced asynchronously via Eventual Consistency and Saga Orchestration.

---

## 5. Non-Functional & Reliability Requirements

### 5.1 Performance & Latency Budgets
- **Catalog Browsing:** $P_{95}$ latency $< 100\text{ ms}$ under 2,000 RPS.
- **Order Ingestion:** $P_{95}$ latency $< 250\text{ ms}$ to accept checkout and return `202 Accepted`.
- **Redis Response:** $P_{99}$ latency $< 15\text{ ms}$ for cart retrieval.

### 5.2 Fault Tolerance & Polly Resilience Policies
- **HTTP/gRPC Interservice Calls:**
  - Circuit Breaker: Breaks after 5 consecutive failures; 30-second break duration.
  - Retries: 3 attempts with exponential backoff ($2^n \times 100\text{ ms}$) and jitter.
  - Timeout: 5-second hard limit on outgoing calls.
- **Database Resilience:** EF Core execution strategies enabled with transient error detection and retry on connection drops.

### 5.3 Distributed Tracing & Observability Standards
- Every HTTP request passing through YARP receives or generates a standard W3C `traceparent` header.
- OpenTelemetry collectors trace spans across Gateway $\rightarrow$ Service $\rightarrow$ RabbitMQ $\rightarrow$ Consumer.
- Logs emitted via Serilog in structured JSON format with `trace_id`, `span_id`, and `service_name` sinking directly to Seq.

### 5.4 Security, RBAC & Claim Verification
- Public endpoints: Browsing products, health probes, registration, login.
- Customer-only endpoints: Basket manipulation, checkout, viewing own orders, cancelling own orders.
- Admin-only endpoints: Creating/updating catalog items, inventory restock, viewing admin real-time metrics.
- Identity validation: Handled at Gateway, verified via claims policies in downstream services.

---

## 6. Hard Verification Scenarios & Failure Criteria

### 6.1 The 6 Distributed Failure Test Definitions
1. **Saga Compensation Test:** Inject card declined code in checkout $\rightarrow$ Assert `PaymentFailedEvent` fired $\rightarrow$ Assert `ReleaseStockCommand` dispatched $\rightarrow$ Assert stock levels in PostgreSQL returned to original values $\rightarrow$ Assert Order marked `Cancelled`.
2. **Service Isolation & Circuit Breaker Test:** Terminate the Inventory Service container $\rightarrow$ Issue checkout request $\rightarrow$ Assert Gateway/Order service catches failure gracefully with `503 Service Unavailable` without unhandled exceptions or service crash.
3. **Idempotency Consumer Test:** Publish duplicate `OrderStartedEvent` with identical `MessageId` $\rightarrow$ Assert Inbox pattern ignores second message $\rightarrow$ Verify stock is reserved only once.
4. **Transactional Outbox Test:** Stop RabbitMQ $\rightarrow$ Submit valid order checkout $\rightarrow$ Verify order record and outbox message are committed to SQL Server $\rightarrow$ Start RabbitMQ $\rightarrow$ Verify background worker drains outbox and publishes event.
5. **Distributed Trace Audit:** Execute complete checkout $\rightarrow$ Query Jaeger API with root trace ID $\rightarrow$ Verify a continuous, unbroken trace hierarchy spanning Gateway, Order, Inventory, Payment, and Notification services.
6. **Concurrency Stress Test:** Run automated script (k6 or NBomber) firing 500 concurrent checkout attempts against a product with exactly 50 available stock $\rightarrow$ Verify exactly 50 orders succeed $\rightarrow$ Verify 450 orders fail with stock rejection $\rightarrow$ Verify database stock does not fall below zero.