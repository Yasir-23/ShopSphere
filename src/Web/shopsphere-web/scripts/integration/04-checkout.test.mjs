import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import {
  CARDS,
  SHIPPING,
  adminToken,
  call,
  createProductWithStock,
  freshCustomer,
  removeProduct,
  waitForOrder,
  waitForStock,
} from "./gateway.mjs";

/**
 * Distributed checkout saga, end to end through the gateway:
 *   POST /orders (with payment method) → Ordering saga → Inventory reserves (PostgreSQL)
 *   → Payment charges → Ordering marks the order Completed (SQL Server) → Inventory commits stock.
 * Decline path: Payment fails → order Cancelled → Inventory releases the reservation.
 */
let admin;
let product;
let customer;

before(async () => {
  admin = await adminToken();
  ({ product } = await createProductWithStock(admin, { quantity: 50, price: 19.5, name: "Saga Test Desk Mat" }));
  customer = await freshCustomer();
});

after(async () => {
  if (product) await removeProduct(admin, product.id);
});

const line = (quantity) => ({ productId: product.id, productName: product.name, unitPrice: product.price, quantity });
const placeOrder = (quantity, paymentMethod, token = customer.token) =>
  call("POST", "/orders", { token, body: { shippingAddress: SHIPPING, items: [line(quantity)], paymentMethod } });

test("Test C — inventory provisioning: new product has a readable 50-unit stock row", async () => {
  const stock = await call("GET", `/inventory/${product.id}`);
  assert.equal(stock.status, 200);
  assert.equal(stock.data.quantityOnHand, 50);
  assert.equal(stock.data.quantityReserved, 0);
  assert.equal(stock.data.quantityAvailable, 50);
});

test("order creation requires authentication and items", async () => {
  assert.equal((await call("POST", "/orders", { body: { shippingAddress: SHIPPING, items: [line(1)] } })).status, 401);
  assert.equal((await call("POST", "/orders", { token: customer.token, body: { shippingAddress: SHIPPING, items: [] } })).status, 400);
});

let completedOrder;
test("Test A — happy path: 2 units with Visa 4242 → Completed, stock 50 → 48", async () => {
  // Cart synced to Redis exactly as the storefront does
  assert.equal((await call("POST", "/basket", { token: customer.token, body: { items: [line(2)] } })).status, 200);

  const created = await placeOrder(2, CARDS.approve);
  assert.equal(created.status, 201, JSON.stringify(created.data));
  assert.equal(created.data.status, "Pending");
  assert.equal(created.data.totalAmount, 39);

  completedOrder = await waitForOrder(customer.token, created.data.id);
  assert.equal(completedOrder.status, "Completed", `cancelled: ${completedOrder.cancellationReason}`);
  assert.equal(completedOrder.cancellationReason ?? null, null);

  const stock = await waitForStock(product.id, (s) => s.quantityReserved === 0 && s.quantityOnHand === 48);
  assert.deepEqual(
    { onHand: stock.quantityOnHand, reserved: stock.quantityReserved, available: stock.quantityAvailable },
    { onHand: 48, reserved: 0, available: 48 }
  );

  const payment = await call("GET", `/payments/${completedOrder.id}`, { token: customer.token });
  assert.equal(payment.data.status, "Completed");
  assert.equal(payment.data.paymentMethod, CARDS.approve);
  assert.match(payment.data.transactionId, /^ch_/);

  assert.equal((await call("DELETE", "/basket", { token: customer.token })).status, 204);
});

test("Test B — compensation: Test Card 0000 → Cancelled (payment declined), stock not decremented", async () => {
  const created = await placeOrder(3, CARDS.decline);
  assert.equal(created.status, 201);

  const order = await waitForOrder(customer.token, created.data.id);
  assert.equal(order.status, "Cancelled");
  assert.equal(order.cancellationReason, "Payment declined: Card Declined");

  // The 3 reserved units are released back: still 48 on hand, nothing reserved.
  const stock = await waitForStock(product.id, (s) => s.quantityReserved === 0);
  assert.equal(stock.quantityOnHand, 48);
  assert.equal(stock.quantityAvailable, 48);

  const payment = await call("GET", `/payments/${order.id}`, { token: customer.token });
  assert.equal(payment.data.status, "Failed");
  assert.equal(payment.data.failureReason, "Card Declined");
});

test("out of stock: ordering more than available → Cancelled, no charge, stock untouched", async () => {
  const created = await placeOrder(1000, CARDS.approve);
  assert.equal(created.status, 201);

  const order = await waitForOrder(customer.token, created.data.id);
  assert.equal(order.status, "Cancelled");
  assert.match(order.cancellationReason, /^Out of stock/);

  const stock = (await call("GET", `/inventory/${product.id}`)).data;
  assert.equal(stock.quantityAvailable, 48);
  const payment = await call("GET", `/payments/${order.id}`, { token: customer.token });
  assert.notEqual(payment.data?.status, "Completed", "no payment taken");
});

test("completed orders can no longer be cancelled", async () => {
  const res = await call("POST", `/orders/${completedOrder.id}/cancel`, { token: customer.token });
  assert.equal(res.status, 400);
  const order = (await call("GET", `/orders/${completedOrder.id}`, { token: customer.token })).data;
  assert.equal(order.status, "Completed");
});

test("a repeat payment for a settled order is refused (409)", async () => {
  const res = await call("POST", "/payments", {
    token: customer.token,
    body: { orderId: completedOrder.id, amount: completedOrder.totalAmount, currency: "USD", paymentMethod: CARDS.approve },
  });
  assert.equal(res.status, 409);
});

test("order history reflects the stored order states", async () => {
  const history = await call("GET", "/orders/my-orders?pageNumber=1&pageSize=10", { token: customer.token });
  assert.equal(history.status, 200);
  assert.equal(history.data.totalCount, 3);
  const statuses = history.data.items.map((o) => o.status);
  assert.deepEqual(statuses, ["Cancelled", "Cancelled", "Completed"], "newest first");
});

test("customers cannot read another customer's order (403)", async () => {
  const other = await freshCustomer();
  assert.equal((await call("GET", `/orders/${completedOrder.id}`, { token: other.token })).status, 403);
});
