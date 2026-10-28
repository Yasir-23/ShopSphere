# Product Requirements Document (PRD)

## Document Details
- **Project Name:** ShopSphere E-Commerce Microservices Platform
- **Document Version:** 1.0.0
- **Status:** Approved / Ready for Architecture & Engineering
- **Target Platform:** .NET 9 / C# 13 Distributed Cloud-Ready Architecture

---

## 1. Executive Summary & Vision

### 1.1 Problem Statement
ShopSphere's legacy e-commerce platform operates as a centralized monolithic web application running on a shared relational database. During scheduled promotional events and peak traffic windows (notably Friday campaigns), systemic resource contention occurs across order placement, stock allocation, and payment processing. A single bottleneck in payment gateways or inventory deadlocks cascades into total platform failure. Furthermore, continuous deployments require full application downtime, while cross-team code contention impedes operational agility.

### 1.2 Product Vision
ShopSphere will transition to an enterprise-grade, event-driven distributed microservices architecture. By decomposing core domains into isolated, autonomous services with dedicated persistence mechanisms, the platform ensures that individual service disruptions do not degrade overarching business operations. 

Key architectural goals include:
- **Zero Cascading Failures:** Faults are localized; peripheral degradation does not halt critical path operations.
- **Data Autonomy:** Polyglot persistence guarantees strict database-per-service boundaries.
- **Eventual Consistency & Atomicity:** Asynchronous saga orchestration guarantees distributed data integrity without distributed locks.
- **Complete Auditability & Observability:** Real-time distributed tracing and telemetry across all transactions.

---

## 2. Target Users & Personas

### 2.1 Customer (Shopper)
- **Role:** End-user purchasing products online.
- **Pain Points:** Application crashes during checkout, lost shopping carts, stale price reflections, and zero visibility into order fulfillment states.
- **Needs:** Fast catalog browsing and search, persistent shopping cart across devices, transparent stock levels, real-time order progression updates, and self-service cancellation capabilities.

### 2.2 Store Administrator
- **Role:** Internal business manager overseeing catalog, inventory, and fulfillment operations.
- **Pain Points:** Database locking during batch product updates, lack of real-time sales visibility, and manual reconciliation overhead for failed orders.
- **Needs:** Real-time operational dashboard (active revenue and throughput metrics), safe inventory modification mechanisms, and auditable order history.

### 2.3 Operations & Platform Engineer (SRE)
- **Role:** Technical maintainer responsible for platform uptime, reliability, and deployments.
- **Pain Points:** Lack of request traceability across services, catastrophic blast radiuses during deployment rollouts, and brittle inter-service dependencies.
- **Needs:** Health checks, telemetry (traces, metrics, structured logs), standardized containerized deployment manifests, and automated resilience mechanisms (circuit breakers, exponential backoff retries).

---

## 3. High-Level System Architecture & Boundaries

The platform comprises seven autonomous backend business services, an edge API gateway, and shared enterprise infrastructure components:

```
+-------------------------------------------------------------------------------+
|                               Edge Clients / UI                               |
+---------------------------------------+---------------------------------------+
                                        | (HTTPS)
                                        v
+-------------------------------------------------------------------------------+
|                       API Gateway (YARP Reverse Proxy)                        |
|        - JWT Offloading  - Rate Limiting  - Route Aggregation & Caching       |
+----+---------------------+--------------------+---------------------+-----+
     |                     |                    |                     |
     | (REST)              | (REST)             | (REST/gRPC)         | (REST)
     v                     v                    v                     v
+------------+      +------------+       +------------+        +------------+
|  Identity  |      |   Basket   |       |  Catalog   |        |   Order    |
|  Service   |      |  Service   |       |  Service   |        |  Service   |
| (SQL Svr)  |      |  (Redis)   |       | (MongoDB/  |        | (SQL Svr)  |
+------------+      +-----+------+       | PostgreSQL)|        +-----+------+
                          |              +------------+              |
                          | (Checkout Event)                         |
                          +---------------------+--------------------+
                                                |
                                                v
               +-------------------------------------------------+
               |    Message Broker: RabbitMQ (via MassTransit)    |
               |       - Outbox Pattern  - Idempotent Inboxes    |
               +-------+-------------------------+---------------+
                       |                         |
                       v                         v
            +---------------------+   +---------------------+
            |  Inventory Service  |   |   Payment Service   |
            |    (PostgreSQL)     |   |    (PostgreSQL)     |
            +---------------------+   +---------------------+
                       |                         |
                       +------------+------------+
                                    |
                                    v
                       +-------------------------+
                       |  Notification Service   |
                       | (SignalR + Async Queue) |
                       +-------------------------+
```

### 3.1 Strict Data Boundary Rules
1. **Database-Per-Service:** Under no circumstances shall any microservice query, inspect, or write to the database tables or datastores of another microservice.
2. **Synchronous Queries (gRPC):** Used strictly for deterministic, read-only inter-service dependencies (e.g., Basket querying Catalog for canonical price verification).
3. **Asynchronous Mutations (Events):** All state-changing cross-service operations must be mediated via MassTransit using RabbitMQ.

---

## 4. Detailed Functional Epics & Capabilities

### Epic 1: Identity & Access Management (Identity Service)
- **User Authentication:** Support email/password registration and login with Argon2/PBKDF2 cryptographic hashing.
- **Token Infrastructure:** Issue short-lived JWT Access Tokens (15-minute validity) coupled with cryptographically random Refresh Tokens (7-day validity) stored in SQL Server with automatic rotation and revocation on reuse detection.
- **Role-Based Access Control (RBAC):** Claim-based authorization enforcing two core roles: `Customer` and `Administrator`.
- **Service Authentication:** Support secure inter-service API key or mTLS authentication for privileged internal requests.

### Epic 2: Product Discovery & Catalog Governance (Catalog Service)
- **Product Administration:** Administrators can create, update, deactivate, and categorize products with rich hierarchical attributes (color, size, weight, brand), image reference links, and currency-denominated base prices.
- **Search & Querying:** Fast indexed queries supporting multi-criteria filtering, keyword search, price range boundary filters, sort criteria (price, newest, name), and cursor/offset-based pagination.
- **Output Caching:** High-performance caching layers at both Gateway and Catalog service levels to absorb heavy read spikes.
- **Price Verification Contract:** Expose a high-performance gRPC endpoint (`GetItemPrices`) for downstream cart price validation.

### Epic 3: Customer Basket Management (Basket Service)
- **Persistent Ephemeral Cart:** Maintain cart items, selected quantities, and transient product metadata keyed by unique `CustomerId` or anonymous tracking token.
- **Sliding Expiration:** Cart records persist in Redis with a rolling 30-day Time-To-Live (TTL), refreshing on each user interaction.
- **Price Synchronization:** Prior to checkout, synchronize and validate customer cart items against the Catalog Service over gRPC to intercept discrepancies between cached cart data and current catalog pricing.
- **Checkout Dispatch:** Package confirmed basket contents into a strongly typed `CheckoutBasketCommand` and publish to the message broker.

### Epic 4: Order Lifecycle & Saga Orchestration (Order Service)
- **Order State Machine:** Manage the end-to-end lifecycle of an order across states:
  - `Submitted` $\rightarrow$ `AwaitingStockReservation` $\rightarrow$ `StockReserved` $\rightarrow$ `PaymentPending` $\rightarrow$ `Confirmed` $\rightarrow$ `Shipped` $\rightarrow$ `Delivered`
  - *Failure branches:* `StockReservationFailed`, `PaymentFailed`, `Cancelled`, `Refunded`.
- **Immutable Price Snapshot:** Capture an exact frozen copy of product names, SKUs, unit prices, applied taxes, and delivery charges at the point of order creation. Future catalog modifications must never alter an existing order record.
- **Customer Isolation:** Customers can only inspect and manage their own orders (`CustomerId` claims derived directly from validated JWT tokens).
- **Self-Service Cancellation:** Permit customer-initiated cancellation exclusively before the order transitions to the `Shipped` state.

### Epic 5: Inventory Allocation & Reservation (Inventory Service)
- **Real-Time Stock Auditing:** Maintain discrete tracking of physical stock vs. allocated (reserved) stock per SKU.
- **Atomic Reservation:** Atomically lock and reserve inventory upon receiving checkout intent. Reject requests if requested quantity exceeds unreserved stock.
- **Compensating Release:** If downstream processes fail (e.g., payment rejected or order cancelled), automatically release reserved stock back to the unallocated pool.

### Epic 6: Payment Processing & Reconciliation (Payment Service)
- **Asynchronous Payment Execution:** Consume payment processing requests emitted by the Order Saga orchestrator.
- **Simulation Engine:** Support deterministic mock payment scenarios (successful authorization, card declined, gateway timeout, fraud hold) via specific metadata flags or mock card numbers.
- **Compensating Refunds:** Support automated refund dispatch instructions when an order is cancelled post-payment authorization.

### Epic 7: Real-Time Notifications & Push Streaming (Notification Service)
- **Asynchronous Notifications:** Consume order milestone events to simulate email, SMS, and transactional receipts.
- **Customer Push Notifications (SignalR):** Maintain an active persistent WebSocket bridge to connected customer clients to push live status changes (e.g., "Your payment was accepted; order is confirmed").
- **Admin Operational Dashboard (SignalR):** Stream live transactional throughput, gross revenue increments, and failed checkout alerts to authorized administrative browser sessions.

### Epic 8: API Gateway & Traffic Ingress (YARP)
- **Reverse Proxy Routing:** Provide unified path routing to back-end services (`/api/v1/catalog`, `/api/v1/orders`, etc.).
- **Centralized Token Offloading:** Validate inbound JWT headers, reject unauthenticated traffic at the edge, and forward sanitized user claims (`X-User-Id`, `X-User-Role`) downstream.
- **Rate Limiting:** Enforce IP-based and user-based token bucket rate limiting to prevent abuse and denial-of-service vulnerabilities.

---

## 5. Non-Functional Requirements (NFRs)

### 5.1 Performance & Scalability
- **Catalog Browsing Latency:** 95th percentile ($P_{95}$) response time $< 100\text{ ms}$ under a baseline load of 2,000 requests per second (RPS).
- **Checkout Ingestion:** Order creation ingestion API must respond within $< 250\text{ ms}$ by offloading fulfillment execution to asynchronous background sagas.
- **Horizontal Elasticity:** All service containers must be strictly stateless, allowing zero-downtime horizontal scaling behind load balancers.

### 5.2 Reliability & Fault Tolerance
- **Saga Compensation Reliability:** Distributed transactions must never leave system state split. 100% of failed payment transactions must trigger stock release commands.
- **Dual-Write Prevention (Transactional Outbox):** All domain state mutations that yield integration events must write to the local database and an Outbox table within the same ACID transaction.
- **Message Idempotency (Inbox Pattern):** Message consumers must verify message IDs against an idempotent store to guarantee that duplicate delivery of messages produces no side effects.
- **Polly Resilience Policies:** Synchronous inter-service HTTP/gRPC pipelines must implement circuit breaking (open circuit after 5 consecutive failures for 30 seconds), timeouts, and exponential backoff retry with jitter.

### 5.3 Observability & Telemetry
- **Distributed Tracing:** Every inbound request at the API Gateway must be assigned a W3C `traceparent` context that propagates across HTTP headers, gRPC metadata, and RabbitMQ message headers to Jaeger/Zipkin.
- **Structured Logging:** All logs must emit structured JSON via Serilog containing `CorrelationId`, `Timestamp`, `Environment`, `ServiceName`, and sanitized request contexts, sinking to Seq.
- **Health Checks:** Every service must expose standard ASP.NET Core endpoints:
  - `/health/live`: Basic liveness probe.
  - `/health/ready`: Deep readiness probe verifying database and broker connectivity.

### 5.4 Security & Compliance
- **Transport Security:** All public and internal inter-service communication must run over TLS 1.3.
- **Zero Raw Secret Storage:** No database passwords, JWT signing keys, or connection strings in source code or unencrypted configuration files. Use environment variables and secrets managers.
- **Input Sanitization:** Strict payload validation using FluentValidation across all incoming DTOs to mitigate SQL injection, XSS, and parameter tampering.

---

## 6. Success Metrics & Distributed Systems Acceptance Criteria

| Area | Acceptance Test / Metric | Target Criteria |
| :--- | :--- | :--- |
| **Saga Compensation** | Inject payment failure during order checkout | Inventory reservation is rolled back in PostgreSQL within $< 2\text{ s}$; Order status set to `Cancelled`. |
| **Service Isolation** | Stop Inventory Service container during live checkout | Gateway/Order Service gracefully yields `503 Service Unavailable` with `ProblemDetails`; Order Service remains responsive. |
| **Idempotency** | Replay identical `OrderCreatedEvent` payload | Message acknowledged; secondary duplicate execution skipped; no duplicate DB records created. |
| **Transactional Outbox** | Stop RabbitMQ broker $\rightarrow$ execute order checkout $\rightarrow$ restart RabbitMQ | Order saved in SQL Server; events safely sit in Outbox table; upon broker restart, events automatically drain and publish. |
| **Distributed Trace** | Complete checkout flow audit in Jaeger | Single unbroken trace spanning: `Gateway` $\rightarrow$ `Order` $\rightarrow$ `Inventory` $\rightarrow$ `Payment` $\rightarrow$ `Notification`. |
| **Concurrency Load Test**| 500 concurrent checkout requests via k6 on a product with exactly 50 units in stock | Exactly 50 orders confirmed; 450 orders rejected with out-of-stock errors; zero deadlocks; stock remains exactly 0. |

---

## 7. Project Boundaries & Exclusions (Out of Scope)

To keep initial implementation focused and robust, the following capabilities are explicitly deferred:
- Physical payment gateway integration (Stripe/PayPal live banking rails). Testing will rely on our simulated payment engine.
- Complex multi-warehouse geographical stock routing.
- Internationalization (i18n), multi-currency conversion, and dynamic localized tax compliance engines.
- Frontend mobile or single-page web app production builds (testing will occur via Swagger UI, automated test suites, and Postman/curl collections).