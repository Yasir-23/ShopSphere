import { test } from "node:test";
import assert from "node:assert/strict";
import { call, freshCustomer } from "./gateway.mjs";

test("Basket: Redis cart sync (add → update quantity → clear)", async (t) => {
  const customer = await freshCustomer();
  const { data } = await call("GET", "/catalog/products?pageSize=100");
  const [a, b] = data.items.filter((p) => p.isAvailable);
  assert.ok(a && b, "need two available products");

  await t.test("anonymous basket access is rejected (401)", async () => {
    const res = await call("GET", "/basket");
    assert.equal(res.status, 401);
  });

  await t.test("a new customer starts with an empty basket", async () => {
    const res = await call("GET", "/basket", { token: customer.token });
    assert.equal(res.status, 200);
    assert.deepEqual(res.data.items, []);
    assert.equal(res.data.totalAmount, 0);
  });

  await t.test("adding an item stores it with the catalog price", async () => {
    const res = await call("POST", "/basket", {
      token: customer.token,
      body: { items: [{ productId: a.id, productName: a.name, unitPrice: a.price, quantity: 1 }] },
    });
    assert.equal(res.status, 200);
    assert.equal(res.data.items.length, 1);
    assert.equal(res.data.items[0].unitPrice, a.price);
  });

  await t.test("updating quantity and adding a second line is persisted", async () => {
    const res = await call("POST", "/basket", {
      token: customer.token,
      body: {
        items: [
          { productId: a.id, productName: a.name, unitPrice: a.price, quantity: 3 },
          { productId: b.id, productName: b.name, unitPrice: b.price, quantity: 1 },
        ],
      },
    });
    assert.equal(res.status, 200);
    const reread = await call("GET", "/basket", { token: customer.token });
    const lineA = reread.data.items.find((i) => i.productId === a.id);
    assert.equal(lineA.quantity, 3);
    assert.equal(reread.data.items.length, 2);
    const expected = Math.round((a.price * 3 + b.price) * 100) / 100;
    assert.equal(Math.round(reread.data.totalAmount * 100) / 100, expected);
  });

  await t.test("client-supplied prices are replaced by verified catalog prices", async () => {
    const res = await call("POST", "/basket", {
      token: customer.token,
      body: { items: [{ productId: a.id, productName: a.name, unitPrice: 0.01, quantity: 1 }] },
    });
    assert.equal(res.status, 200);
    assert.equal(res.data.items[0].unitPrice, a.price);
  });

  await t.test("unknown products are refused", async () => {
    const res = await call("POST", "/basket", {
      token: customer.token,
      body: { items: [{ productId: "000000000000000000000000", productName: "Ghost", unitPrice: 1, quantity: 1 }] },
    });
    assert.equal(res.status, 400);
  });

  await t.test("clearing the basket empties it", async () => {
    const del = await call("DELETE", "/basket", { token: customer.token });
    assert.equal(del.status, 204);
    const reread = await call("GET", "/basket", { token: customer.token });
    assert.deepEqual(reread.data.items, []);
  });
});
