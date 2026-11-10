/**
 * Shared helpers for the storefront's gateway contract tests.
 * They exercise the exact endpoints and payloads the Next.js app uses.
 */

export const GATEWAY = (process.env.GATEWAY_URL || "http://localhost:5001/api/v1").replace(/\/$/, "");

export const DEMO = {
  customer: { email: "customer@shopsphere.local", password: "SecurePassword123!" },
  admin: { email: "admin@shopsphere.local", password: "AdminPassword123!" },
};

/** Same tokens the checkout modal sends (src/lib/orders.ts PAYMENT_OPTIONS). */
export const CARDS = { approve: "card_visa_4242", decline: "card_test_0000" };

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/**
 * fetch wrapper: JSON in/out, Bearer token, and polite back-off on the
 * gateway's fixed-window rate limiter (HTTP 429).
 */
export async function call(method, path, { token, body, attempts = 8 } = {}) {
  const headers = { Accept: "application/json" };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (token) headers.Authorization = `Bearer ${token}`;

  for (let i = 0; ; i++) {
    const res = await fetch(`${GATEWAY}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (res.status === 429 && i < attempts) {
      const wait = Number(res.headers.get("retry-after")) * 1000 || 8000;
      console.log(`  (rate limited on ${method} ${path}; waiting ${wait / 1000}s)`);
      await sleep(wait);
      continue;
    }
    const text = await res.text();
    let data = null;
    try {
      data = text ? JSON.parse(text) : null;
    } catch {
      data = text;
    }
    return { status: res.status, data };
  }
}

export async function login(email, password) {
  const res = await call("POST", "/identity/login", { body: { email, password } });
  if (res.status !== 200) throw new Error(`Login failed for ${email}: HTTP ${res.status} ${JSON.stringify(res.data)}`);
  return res.data;
}

export function uniqueSuffix() {
  return `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
}

/** Register and sign in a throwaway customer so tests never touch demo data. */
export async function freshCustomer() {
  const email = `it-${uniqueSuffix()}@shopsphere.test`;
  const password = "Str0ng!Passw0rd";
  const reg = await call("POST", "/identity/register", {
    body: { email, password, firstName: "Integration", lastName: "Tester" },
  });
  if (reg.status !== 201) throw new Error(`Register failed: HTTP ${reg.status} ${JSON.stringify(reg.data)}`);
  const tokens = await login(email, password);
  return { email, password, token: tokens.accessToken, refreshToken: tokens.refreshToken };
}

/** First product that is on sale and has stock available. */
export async function pickPurchasableProduct() {
  const { data } = await call("GET", "/catalog/products?pageNumber=1&pageSize=100");
  for (const p of data.items) {
    if (!p.isAvailable) continue;
    const stock = await call("GET", `/inventory/${p.id}`);
    if (stock.status === 200 && stock.data.quantityAvailable > 0) return { product: p, stock: stock.data };
  }
  throw new Error("No purchasable product with stock found in the catalog.");
}

export async function adminToken() {
  return (await login(DEMO.admin.email, DEMO.admin.password)).accessToken;
}

/** Admin flow from the dashboard: create the catalog product, then provision its stock. */
export async function createProductWithStock(token, { quantity = 50, price = 25, name } = {}) {
  const sku = `IT-${uniqueSuffix()}`.toUpperCase();
  const product = await call("POST", "/catalog/products", {
    token,
    body: {
      name: name ?? `Integration Widget ${sku}`,
      description: "Created by the storefront integration suite.",
      sku,
      price,
      currency: "USD",
      category: "Electronics",
      images: [],
      isAvailable: true,
    },
  });
  if (product.status !== 201) throw new Error(`Product creation failed: HTTP ${product.status} ${JSON.stringify(product.data)}`);
  const stock = await call("POST", "/inventory/items", { token, body: { productId: product.data.id, sku, quantity } });
  if (stock.status !== 201) throw new Error(`Stock provisioning failed: HTTP ${stock.status} ${JSON.stringify(stock.data)}`);
  return { product: product.data, stock: stock.data };
}

export async function removeProduct(token, productId) {
  await call("DELETE", `/inventory/items/${productId}`, { token });
  await call("DELETE", `/catalog/products/${productId}`, { token });
}

/** Follow the order aggregate until the checkout saga settles it (Completed / Cancelled). */
export async function waitForOrder(token, orderId, timeoutMs = 30000) {
  const deadline = Date.now() + timeoutMs;
  let order;
  while (Date.now() < deadline) {
    order = (await call("GET", `/orders/${orderId}`, { token })).data;
    if (order?.status === "Completed" || order?.status === "Cancelled") return order;
    await sleep(400);
  }
  throw new Error(`Order ${orderId} did not settle within ${timeoutMs}ms (last status: ${order?.status})`);
}

/** Poll stock until a predicate holds (commit/release happen asynchronously after the order settles). */
export async function waitForStock(productId, predicate, timeoutMs = 15000) {
  const deadline = Date.now() + timeoutMs;
  let stock;
  while (Date.now() < deadline) {
    stock = (await call("GET", `/inventory/${productId}`)).data;
    if (predicate(stock)) return stock;
    await sleep(300);
  }
  throw new Error(`Stock for ${productId} did not reach the expected state: ${JSON.stringify(stock)}`);
}

export const SHIPPING = {
  street: "100 Market Street",
  city: "San Francisco",
  state: "CA",
  zipCode: "94105",
  country: "United States",
};
