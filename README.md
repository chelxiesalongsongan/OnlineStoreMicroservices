# Online Store Microservices

Catalog, Order, Inventory and Payment services plus an MVC storefront. Placing an order runs as a saga with compensation. Runs without Docker.

## Architecture
| Project | Role | Port | Database |
|---|---|---|---|
| CatalogService | Products CRUD (`/catalog/v1/products`), called synchronously by Order | 5047 | SQLite `CatalogDB.db` |
| OrderService | Orders CRUD (`/orders/v1/orders`), validates via Catalog, publishes `OrderPlaced`, saga orchestrator | 5102 | SQLite `order.db` |
| InventoryService | Stock CRUD (`/inventory/v1/stock`), reserves/releases stock on events | 5104 | SQLite `inventory.db` |
| PaymentService | Payments CRUD (`/payments/v1/payments`), charge and refund | 5101 | SQLite `payment.db` |
| Storefront | MVC web app: browse products, place an order, follow its status | 5100 | none |
| OnlineStore.Shared | Event contracts, correlation ID middleware/handler, RabbitMQ helpers | | |

Each service owns its database and exchanges data only through HTTP APIs and RabbitMQ events. OpenAPI contracts are in `contracts/`. Swagger UI is at `/swagger` on every service. Errors use Problem Details. APIs use URI versioning (`/catalog/v1`, `/orders/v1`, `/inventory/v1`, `/payments/v1`).

## Order flow
1. Storefront creates a correlation ID and calls `POST /orders/v1/orders`.
2. Order validates products with Catalog (typed `HttpClient`, retry with exponential backoff and jitter, per-attempt and total timeouts).
3. Order saves the order as `Pending` and publishes `OrderPlaced` (publisher confirms).
4. Inventory consumes it idempotently, reserves stock, and publishes `StockReserved` or `StockReservationFailed`.
5. Order's saga consumer charges Payment through an NSwag-generated client. Success: order `Confirmed`.
6. **Compensation:** if payment is declined or stock is insufficient, the order becomes `Cancelled` and a `StockReleaseRequested` event makes Inventory free the reserved units. A confirmed order can be cancelled on demand (`POST /orders/v1/orders/{id}/cancel`): refund plus stock release.
7. Failing messages are retried once, then dead-lettered to `<queue>.dlq`.

## Prerequisites
- .NET 8 SDK or newer, with the ASP.NET Core 8 runtime
- RabbitMQ with the management plugin (native install, Erlang 27 or 28; no Docker)
- `dotnet tool install -g dotnet-ef`

## Setup
1. Install and start RabbitMQ (management UI: http://localhost:15672, login `guest` / `guest` on localhost).
2. Set broker credentials as user secrets, so none are committed:
dotnet user-secrets set "RabbitMq:UserName" "guest" --project OrderService
dotnet user-secrets set "RabbitMq:Password" "guest" --project OrderService
dotnet user-secrets set "RabbitMq:UserName" "guest" --project InventoryService
dotnet user-secrets set "RabbitMq:Password" "guest" --project InventoryService

3. Databases are created and seeded automatically at startup by EF Core migrations. To add a migration: `dotnet ef migrations add <Name> --project <Service>`.

## Run
Build once with `dotnet build OnlineStore.sln`, then start each service in its own terminal:
dotnet run --project CatalogService --urls http://localhost:5047
dotnet run --project PaymentService --urls http://localhost:5101
dotnet run --project OrderService --urls http://localhost:5102
dotnet run --project InventoryService --urls http://localhost:5104
dotnet run --project Storefront --urls http://localhost:5100

Open http://localhost:5100. Start Order and Inventory once before placing orders, so both declare their queues. Swagger: http://localhost:5047/swagger, :5101, :5102, :5104.

## Demonstrating compensation
- **Declined payment:** open "Demo options" on the shop page and tick "Simulate payment failure". Payment returns 402, the order is cancelled and the stock is released.
- **On demand:** "Cancel & refund" on a confirmed order, or `POST /orders/v1/orders/{id}/cancel`.
- **Insufficient stock:** order more units than are in stock (via the API). Inventory rejects the reservation and the order is cancelled.
- **Dead-letter queue:** publish a malformed message to `store.events`; it is retried once and then appears in `<queue>.dlq` (see the RabbitMQ UI).
- `scripts/demo-helpers.ps1` contains PowerShell helpers for placing orders and simulating events.

## Consistency
Between "order saved" and "payment charged" the system is **eventually consistent**: an order is briefly `Pending` or `StockReserved` while stock is already held. This is acceptable because the saga always ends in `Confirmed` or `Cancelled`, every step is idempotent (processed-event table in Inventory, status guard in Order, one payment per order), and every step has a compensation. Known gap: saving the order and publishing the event are not atomic. A transactional outbox would close it.

## Secrets
No credentials are committed. Use user-secrets or environment variables.
