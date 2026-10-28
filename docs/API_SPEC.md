# API Specification & Communication Contracts

## Document Details

* **Project Name:** ShopSphere E-Commerce Microservices Platform
* **Document Version:** 1.0.0
* **Status:** Approved / Engineering Baseline
* **Base Gateway URL:** `https://api.shopsphere.local` (Local: `https://localhost:5000`)
* **Format:** REST (JSON), gRPC (HTTP/2 Protobuf), WebSockets (SignalR)

---

## 1. Global Standards & Conventions

### 1.1 Content Negotiation & Serialization
* All REST requests and responses use `application/json` with `camelCase` property naming.
* All timestamps follow ISO 8601 UTC format: `YYYY-MM-DDTHH:mm:ss.fffZ`.
* All monetary values are represented as numbers with 2 decimal precision (e.g., `129.99`), coupled with an ISO 4217 currency code (e.g., `"USD"`).

### 1.2 Authentication Headers
* External requests passed to API Gateway:
  ```http
  Authorization: Bearer <jwt-token>
  ```
* Gateway sanitizes and injects downstream identity headers:
  ```http
  X-User-Id: 3fa85f64-5717-4562-b3fc-2c963f66afa6
  X-User-Email: customer@shopsphere.local
  X-User-Roles: Customer
  X-Correlation-Id: c8b9e693-1b9c-4613-8a3b-74b7e8ff4cb8
  ```

### 1.3 Standard Error Format (RFC 7807 Problem Details)
All 4xx and 5xx responses conform to RFC 7807:
```json
{
  "type": "https://shopsphere.local/errors/validation-failed",
  "title": "Validation Failed",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "instance": "/api/v1/catalog/products",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errors": {
    "price": ["Price must be greater than zero."],
    "name": ["Product name is required."]
  }
}
```

---

## 2. Identity Service Endpoints (`/api/v1/identity`)

### 2.1 User Registration
* **Endpoint:** `POST /api/v1/identity/register`
* **Auth:** Anonymous
* **Rate Limit:** 10 req/min per IP
* **Request Body:**
```json
{
  "email": "customer@example.com",
  "password": "SecurePassword123!",
  "firstName": "John",
  "lastName": "Doe"
}
```
* **Responses:**
  * `201 Created`: User account successfully initialized.
    ```json
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "email": "customer@example.com",
      "firstName": "John",
      "lastName": "Doe"
    }
    ```
  * `400 Bad Request`: Validation failure.
  * `409 Conflict`: Email address is already registered.

### 2.2 User Login
* **Endpoint:** `POST /api/v1/identity/login`
* **Auth:** Anonymous
* **Rate Limit:** 10 req/min per IP
* **Request Body:**
```json
{
  "email": "customer@example.com",
  "password": "SecurePassword123!"
}
```
* **Responses:**
  * `200 OK`:
    ```json
    {
      "accessToken": "eyJhbGciOiJIUzI1NiIsInR5c...",
      "expiresIn": 900,
      "refreshToken": "d7a4e68e-9025-4c07-b2f7-ec8f38bc602b",
      "tokenType": "Bearer"
    }
    ```
  * `401 Unauthorized`: Invalid credentials.

### 2.3 Refresh Token Rotation
* **Endpoint:** `POST /api/v1/identity/refresh`
* **Auth:** Anonymous
* **Request Body:**
```json
{
  "refreshToken": "d7a4e68e-9025-4c07-b2f7-ec8f38bc602b"
}
```
* **Responses:**
  * `200 OK`: Returns new access token and newly rotated refresh token.
  * `401 Unauthorized`: Invalid, expired, or revoked refresh token.

---

## 3. Catalog Service Endpoints (`/api/v1/catalog`)

### 3.1 Paginated Product Listing
* **Endpoint:** `GET /api/v1/catalog/products`
* **Auth:** Public
* **Query Parameters:**
  * `pageNumber` (int, default `1`)
  * `pageSize` (int, default `20`, max `100`)
  * `searchTerm` (string, optional)
  * `categoryId` (guid, optional)
  * `minPrice` (decimal, optional)
  * `maxPrice` (decimal, optional)
  * `sortBy` (`price_asc`, `price_desc`, `name_asc`, `newest`)
* **Responses:**
  * `200 OK`:
    ```json
    {
      "items": [
        {
          "id": "660c1d2e5a4b3c2d1e0f9a8b",
          "name": "Wireless Mechanical Keyboard",
          "slug": "wireless-mechanical-keyboard",
          "description": "RGB mechanical keyboard with red switches",
          "price": 89.99,
          "currency": "USD",
          "categoryId": "1a2b3c4d-5e6f-7a8b-9c0d-1e2f3a4b5c6d",
          "attributes": {
            "switchType": "Red",
            "connectivity": "Wireless"
          },
          "imageUrl": "https://cdn.shopsphere.local/keyboard.jpg",
          "isActive": true
        }
      ],
      "pageNumber": 1,
      "pageSize": 20,
      "totalCount": 1,
      "totalPages": 1
    }
    ```

### 3.2 Get Product By ID
* **Endpoint:** `GET /api/v1/catalog/products/{id}`
* **Auth:** Public
* **Responses:**
  * `200 OK`: Product details.
  * `404 Not Found`: Product not found.

### 3.3 Create Product
* **Endpoint:** `POST /api/v1/catalog/products`
* **Auth:** Role: `Admin`
* **Request Body:**
```json
{
  "name": "Wireless Mechanical Keyboard",
  "description": "RGB mechanical keyboard with red switches",
  "price": 89.99,
  "currency": "USD",
  "categoryId": "1a2b3c4d-5e6f-7a8b-9c0d-1e2f3a4b5c6d",
  "attributes": {
    "switchType": "Red",
    "connectivity": "Wireless"
  },
  "imageUrl": "https://cdn.shopsphere.local/keyboard.jpg"
}
```
* **Responses:**
  * `201 Created`: Returns newly created product with generated `id`.
  * `400 Bad Request`: Validation failure.
  * `401 Unauthorized` / `403 Forbidden`: Admin role required.

---

## 4. Basket Service Endpoints (`/api/v1/basket`)

### 4.1 Get Customer Cart
* **Endpoint:** `GET /api/v1/basket`
* **Auth:** Role: `Customer`
* **Responses:**
  * `200 OK`:
    ```json
    {
      "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "items": [
        {
          "productId": "660c1d2e5a4b3c2d1e0f9a8b",
          "productName": "Wireless Mechanical Keyboard",
          "unitPrice": 89.99,
          "quantity": 2
        }
      ],
      "totalAmount": 179.98
    }
    ```

### 4.2 Upsert Cart Items
* **Endpoint:** `POST /api/v1/basket`
* **Auth:** Role: `Customer`
* **Request Body:**
```json
{
  "items": [
    {
      "productId": "660c1d2e5a4b3c2d1e0f9a8b",
      "productName": "Wireless Mechanical Keyboard",
      "unitPrice": 89.99,
      "quantity": 2
    }
  ]
}
```
* **Responses:**
  * `200 OK`: Returns updated basket state with recalculated totals.
  * `400 Bad Request`: Invalid item structure or non-positive quantities.

### 4.3 Delete Customer Cart
* **Endpoint:** `DELETE /api/v1/basket`
* **Auth:** Role: `Customer`
* **Responses:**
  * `204 No Content`: Cart successfully cleared.

### 4.4 Checkout Basket
* **Endpoint:** `POST /api/v1/basket/checkout`
* **Auth:** Role: `Customer`
* **Request Body:**
```json
{
  "shippingAddress": {
    "street": "123 Tech Boulevard",
    "city": "Seattle",
    "state": "WA",
    "zipCode": "98101",
    "country": "USA"
  },
  "paymentCard": {
    "cardNumber": "4111111111111234",
    "cardHolderName": "John Doe",
    "expiration": "12/28",
    "cvv": "123"
  }
}
```
* **Responses:**
  * `202 Accepted`: Checkout initiated and handed off to Order Saga. Returns `orderId`.
    ```json
    {
      "orderId": "7e34ef21-692a-4422-9cb4-8a4db9fe2a11",
      "status": "Submitted",
      "message": "Order checkout is being processed."
    }
    ```
  * `409 Conflict`: Cart prices out of sync with catalog. Cart is refreshed with updated prices.

---

## 5. Ordering Service Endpoints (`/api/v1/orders`)

### 5.1 Get Customer Orders
* **Endpoint:** `GET /api/v1/orders`
* **Auth:** Role: `Customer`
* **Query Parameters:** `pageNumber` (int), `pageSize` (int)
* **Responses:**
  * `200 OK`: List of customer orders.

### 5.2 Get Order By ID
* **Endpoint:** `GET /api/v1/orders/{id}`
* **Auth:** Role: `Customer` (must own order) or `Admin`
* **Responses:**
  * `200 OK`:
    ```json
    {
      "id": "7e34ef21-692a-4422-9cb4-8a4db9fe2a11",
      "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "orderDate": "2026-10-28T16:50:00.000Z",
      "status": "Confirmed",
      "totalAmount": 179.98,
      "items": [
        {
          "productId": "660c1d2e5a4b3c2d1e0f9a8b",
          "productName": "Wireless Mechanical Keyboard",
          "unitPrice": 89.99,
          "quantity": 2
        }
      ],
      "shippingAddress": {
        "street": "123 Tech Boulevard",
        "city": "Seattle",
        "state": "WA",
        "zipCode": "98101",
        "country": "USA"
      }
    }
    ```
  * `403 Forbidden`: User attempting to view another customer's order.
  * `404 Not Found`: Order not found.

### 5.3 Cancel Order
* **Endpoint:** `POST /api/v1/orders/{id}/cancel`
* **Auth:** Role: `Customer`
* **Responses:**
  * `200 OK`: Order cancelled successfully; compensation saga initiated.
  * `400 Bad Request`: Order is already `Shipped` or `Delivered` and cannot be cancelled.

---

## 6. Inventory Service Endpoints (`/api/v1/inventory`)

### 6.1 Check Stock Level
* **Endpoint:** `GET /api/v1/inventory/{sku}`
* **Auth:** Public or Internal
* **Responses:**
  * `200 OK`:
    ```json
    {
      "sku": "KB-WL-RED-001",
      "availableQuantity": 150,
      "reservedQuantity": 12
    }
    ```
  * `404 Not Found`: SKU not found.

### 6.2 Restock Inventory
* **Endpoint:** `POST /api/v1/inventory/restock`
* **Auth:** Role: `Admin`
* **Request Body:**
```json
{
  "sku": "KB-WL-RED-001",
  "quantity": 100
}
```
* **Responses:**
  * `200 OK`: Inventory updated.
  * `400 Bad Request`: Non-positive quantity.

---

## 7. gRPC Service Contracts

### 7.1 Catalog Price Checker (`catalog.proto`)
Used by Basket Service to validate live prices prior to checkout:

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

---

## 8. Real-Time Hubs (SignalR / WebSockets)

### 8.1 Customer Order Updates Hub (`/hubs/orders`)
* **Transport:** WebSockets / Long Polling
* **Auth:** JWT Query Parameter (`?access_token=<token>`)
* **Server-to-Client Events:**
  * `OrderStatusChanged`:
    ```json
    {
      "orderId": "7e34ef21-692a-4422-9cb4-8a4db9fe2a11",
      "previousStatus": "AwaitingStock",
      "currentStatus": "Confirmed",
      "timestamp": "2026-10-28T16:50:05.120Z"
    }
    ```

### 8.2 Admin Platform Metrics Hub (`/hubs/admin`)
* **Auth:** Role `Admin`
* **Server-to-Client Events:**
  * `LiveMetricsUpdated`:
    ```json
    {
      "ordersPerMinute": 42,
      "activeCheckouts": 15,
      "grossRevenueToday": 14250.75,
      "failedPaymentsCount": 2
    }
    ```

---

## 9. Asynchronous Integration Events (MassTransit / RabbitMQ)

All events implement standard interfaces and route through MassTransit Outbox:

### 9.1 `OrderStartedEvent`
* **Emitted By:** Order Service
* **Consumed By:** Inventory Service
```json
{
  "orderId": "7e34ef21-692a-4422-9cb4-8a4db9fe2a11",
  "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "items": [
    {
      "sku": "KB-WL-RED-001",
      "quantity": 2
    }
  ],
  "totalAmount": 179.98,
  "timestamp": "2026-10-28T16:50:01.000Z"
}
```

### 9.2 `StockReservedEvent` & `StockReservationFailedEvent`
* **Emitted By:** Inventory Service
* **Consumed By:** Order Service (Saga State Machine)

### 9.3 `ProcessPaymentCommand`
* **Emitted By:** Order Service (Saga Orchestration)
* **Consumed By:** Payment Service

### 9.4 `PaymentCompletedEvent` & `PaymentFailedEvent`
* **Emitted By:** Payment Service
* **Consumed By:** Order Service (Saga State Machine)

### 9.5 `ReleaseStockCommand` (Compensating Transaction)
* **Emitted By:** Order Service (when payment fails or order is cancelled)
* **Consumed By:** Inventory Service (restores `reservedQuantity` to `availableQuantity`)