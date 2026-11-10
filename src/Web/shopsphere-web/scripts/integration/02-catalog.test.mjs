import { test } from "node:test";
import assert from "node:assert/strict";
import { DEMO, call, freshCustomer, login, uniqueSuffix } from "./gateway.mjs";

test("Catalog: browse, search, stock levels and admin product management", async (t) => {
  let products;
  let adminToken;
  let created;

  await t.test("lists products with the fields product cards render", async () => {
    const res = await call("GET", "/catalog/products?pageNumber=1&pageSize=100");
    assert.equal(res.status, 200);
    products = res.data.items;
    assert.ok(products.length > 0, "catalog has products");
    for (const p of products) {
      assert.equal(typeof p.id, "string");
      assert.equal(typeof p.name, "string");
      assert.equal(typeof p.price, "number");
      assert.equal(typeof p.category, "string");
      assert.equal(typeof p.isAvailable, "boolean");
      assert.ok(Array.isArray(p.images));
    }
  });

  await t.test("lists categories for navigation", async () => {
    const res = await call("GET", "/catalog/categories");
    assert.equal(res.status, 200);
    assert.ok(res.data.length > 0);
    assert.ok(res.data.every((c) => c.id && c.name));
  });

  await t.test("server-side search and category filter agree with the client-side filter", async () => {
    const sample = products[0];
    const term = sample.name.split(" ")[0];
    const res = await call("GET", `/catalog/products?pageSize=100&searchTerm=${encodeURIComponent(term)}`);
    assert.equal(res.status, 200);
    const clientSide = products.filter((p) =>
      [p.name, p.description, p.sku].some((f) => f.toLowerCase().includes(term.toLowerCase()))
    );
    assert.deepEqual(res.data.items.map((p) => p.id).sort(), clientSide.map((p) => p.id).sort());

    const byCat = await call("GET", `/catalog/products?pageSize=100&category=${encodeURIComponent(sample.category)}`);
    assert.ok(byCat.data.items.length > 0);
    assert.ok(byCat.data.items.every((p) => p.category === sample.category));
  });

  await t.test("inventory returns live stock for stocked catalog products", async () => {
    // Not every catalog product has a stock record (new admin products don't);
    // the storefront shows those as "Available to order". At least one must be stocked.
    let stocked = 0;
    for (const p of products) {
      const res = await call("GET", `/inventory/${p.id}`);
      assert.ok([200, 404].includes(res.status), `unexpected ${res.status} for ${p.id}`);
      if (res.status === 200) {
        stocked++;
        assert.equal(res.data.productId, p.id);
        assert.equal(typeof res.data.quantityAvailable, "number");
        assert.equal(typeof res.data.restockThreshold, "number");
      }
    }
    assert.ok(stocked > 0, "at least one product has a stock record");
  });

  await t.test("inventory returns 404 for a product without a stock record", async () => {
    const res = await call("GET", "/inventory/does-not-exist-000");
    assert.equal(res.status, 404);
  });

  await t.test("customers cannot create products (403)", async () => {
    const customer = await freshCustomer();
    const res = await call("POST", "/catalog/products", {
      token: customer.token,
      body: { name: "Nope", description: "x", sku: `NOPE-${uniqueSuffix()}`, price: 1, category: "Electronics" },
    });
    assert.equal(res.status, 403);
  });

  await t.test("admin creates a product (201) and it is visible in the catalog", async () => {
    adminToken = (await login(DEMO.admin.email, DEMO.admin.password)).accessToken;
    const sku = `IT-${uniqueSuffix()}`.toUpperCase();
    const res = await call("POST", "/catalog/products", {
      token: adminToken,
      body: {
        name: "Integration Test Charging Stand",
        description: "Created by the storefront integration suite.",
        sku,
        price: 39.5,
        currency: "USD",
        category: "Electronics",
        imageUrl: null,
        images: [],
        isAvailable: true,
      },
    });
    assert.equal(res.status, 201, JSON.stringify(res.data));
    created = res.data;
    assert.equal(created.sku, sku);
    assert.equal(created.price, 39.5);

    const fetched = await call("GET", `/catalog/products/${created.id}`);
    assert.equal(fetched.status, 200);
    assert.equal(fetched.data.name, "Integration Test Charging Stand");
  });

  await t.test("duplicate SKUs are rejected with a readable message", async () => {
    const res = await call("POST", "/catalog/products", {
      token: adminToken,
      body: { name: "Dup", description: "Dup", sku: created.sku, price: 1, category: "Electronics" },
    });
    assert.equal(res.status, 400);
    assert.match(JSON.stringify(res.data.errors), /SKU already exists/i);
  });

  await t.test("admin deletes the product (204) and it is gone (404)", async () => {
    const del = await call("DELETE", `/catalog/products/${created.id}`, { token: adminToken });
    assert.equal(del.status, 204);
    const gone = await call("GET", `/catalog/products/${created.id}`);
    assert.equal(gone.status, 404);
  });
});

test("Admin product lifecycle: create with stock → edit → adjust stock → delete", async (t) => {
  const token = (await login(DEMO.admin.email, DEMO.admin.password)).accessToken;
  const sku = `IT-LIFE-${uniqueSuffix()}`.toUpperCase();
  let productId;

  await t.test("create product, then provision 50 units of stock (201)", async () => {
    const created = await call("POST", "/catalog/products", {
      token,
      body: { name: "Lifecycle Lamp", description: "Lifecycle test", sku, price: 30, currency: "USD", category: "Smart Home", images: [], isAvailable: true },
    });
    assert.equal(created.status, 201);
    productId = created.data.id;

    const stock = await call("POST", "/inventory/items", { token, body: { productId, sku, quantity: 50 } });
    assert.equal(stock.status, 201, JSON.stringify(stock.data));
    assert.equal(stock.data.quantityAvailable, 50);
    assert.equal((await call("GET", `/inventory/${productId}`)).data.quantityAvailable, 50, "visible to the storefront immediately");
  });

  await t.test("stock provisioning is admin-only and refuses duplicates", async () => {
    const customer = await freshCustomer();
    assert.equal((await call("POST", "/inventory/items", { token: customer.token, body: { productId, sku, quantity: 1 } })).status, 403);
    assert.equal((await call("POST", "/inventory/items", { token, body: { productId, sku, quantity: 1 } })).status, 409);
  });

  await t.test("edit product details (PUT)", async () => {
    const updated = await call("PUT", `/catalog/products/${productId}`, {
      token,
      body: { name: "Lifecycle Lamp Pro", description: "Updated", sku, price: 34.5, currency: "USD", category: "Smart Home", images: [], isAvailable: true },
    });
    assert.equal(updated.status, 200, JSON.stringify(updated.data));
    assert.equal(updated.data.name, "Lifecycle Lamp Pro");
    assert.equal((await call("GET", `/catalog/products/${productId}`)).data.price, 34.5);
  });

  await t.test("adjust stock: +10, then −15, never below zero", async () => {
    const add = await call("POST", "/inventory/adjust", { token, body: { productId, quantity: 10 } });
    assert.equal(add.status, 200);
    assert.equal(add.data.quantityOnHand, 60);

    const remove = await call("POST", "/inventory/adjust", { token, body: { productId, quantity: -15 } });
    assert.equal(remove.status, 200);
    assert.equal(remove.data.quantityOnHand, 45);

    const tooMuch = await call("POST", "/inventory/adjust", { token, body: { productId, quantity: -1000 } });
    assert.equal(tooMuch.status, 409);
  });

  await t.test("concurrent +1 adjustments are all applied", async () => {
    const results = await Promise.all(
      Array.from({ length: 10 }, () => call("POST", "/inventory/adjust", { token, body: { productId, quantity: 1 } }))
    );
    assert.ok(results.every((r) => r.status === 200), JSON.stringify(results.map((r) => r.status)));
    assert.equal((await call("GET", `/inventory/${productId}`)).data.quantityOnHand, 55);
  });

  await t.test("delete stock record and product", async () => {
    assert.equal((await call("DELETE", `/inventory/items/${productId}`, { token })).status, 204);
    assert.equal((await call("DELETE", `/catalog/products/${productId}`, { token })).status, 204);
    assert.equal((await call("GET", `/inventory/${productId}`)).status, 404);
  });
});
