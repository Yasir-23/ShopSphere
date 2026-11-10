#!/usr/bin/env node
/**
 * Browser end-to-end test for the ShopSphere storefront.
 *
 * Drives a real headless Chrome/Edge over the DevTools Protocol (no extra
 * dependencies) against a running storefront + gateway, and fails on any
 * uncaught page exception or console error (incl. React hydration errors).
 *
 *   BASE_URL     storefront origin (default http://localhost:5173 — must be an
 *                origin the gateway's CORS policy allows: :3000 or :5173)
 *   GATEWAY_URL  gateway API base (default http://localhost:5001/api/v1)
 *   BROWSER      path to chrome/msedge (auto-detected on Windows/macOS/Linux)
 */
import { spawn } from "node:child_process";
import { existsSync, mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import assert from "node:assert/strict";

const BASE_URL = (process.env.BASE_URL || "http://localhost:5173").replace(/\/$/, "");
const GATEWAY = (process.env.GATEWAY_URL || "http://localhost:5001/api/v1").replace(/\/$/, "");
const PORT = 9300 + Math.floor(Math.random() * 500);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const BROWSER_CANDIDATES = [
  process.env.BROWSER,
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "C:/Program Files/Microsoft/Edge/Application/msedge.exe",
  "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
  "/usr/bin/google-chrome",
  "/usr/bin/chromium",
  "/usr/bin/chromium-browser",
].filter(Boolean);

// ---------------------------------------------------------------------------
// Minimal CDP client
// ---------------------------------------------------------------------------
class Page {
  constructor(ws) {
    this.ws = ws;
    this.id = 0;
    this.pending = new Map();
    this.listeners = new Map();
    this.errors = [];
    ws.addEventListener("message", (ev) => {
      const msg = JSON.parse(typeof ev.data === "string" ? ev.data : ev.data.toString());
      if (msg.id && this.pending.has(msg.id)) {
        const { resolve, reject } = this.pending.get(msg.id);
        this.pending.delete(msg.id);
        if (msg.error) reject(new Error(msg.error.message));
        else resolve(msg.result);
      } else if (msg.method) {
        (this.listeners.get(msg.method) ?? []).forEach((fn) => fn(msg.params));
      }
    });
  }
  send(method, params = {}) {
    const id = ++this.id;
    this.ws.send(JSON.stringify({ id, method, params }));
    return new Promise((resolve, reject) => this.pending.set(id, { resolve, reject }));
  }
  on(method, fn) {
    this.listeners.set(method, [...(this.listeners.get(method) ?? []), fn]);
  }
  async eval(expression) {
    const res = await this.send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
    if (res.exceptionDetails) throw new Error(`eval failed: ${res.exceptionDetails.exception?.description ?? res.exceptionDetails.text}`);
    return res.result.value;
  }
  async waitFor(expression, { timeout = 20000, label = expression } = {}) {
    const deadline = Date.now() + timeout;
    let last;
    while (Date.now() < deadline) {
      try {
        last = await this.eval(expression);
        if (last) return last;
      } catch {
        /* page may be mid-navigation */
      }
      await sleep(150);
    }
    throw new Error(`Timed out waiting for: ${label} (last value: ${JSON.stringify(last)})`);
  }
  async goto(path) {
    await this.send("Page.navigate", { url: BASE_URL + path });
    await this.waitFor("document.readyState === 'complete'", { label: `load ${path}` });
  }
  url() {
    return this.eval("location.pathname + location.search");
  }
  text() {
    return this.eval("document.body.innerText");
  }
  hasText(t) {
    return this.eval(`document.body.innerText.includes(${JSON.stringify(t)})`);
  }
  waitForText(t, timeout) {
    return this.waitFor(`document.body.innerText.includes(${JSON.stringify(t)})`, { timeout, label: `text "${t}"` });
  }
  click(selector, text) {
    return this.eval(`__click(${JSON.stringify(selector)}, ${JSON.stringify(text ?? null)})`);
  }
  fill(selector, value) {
    return this.eval(`__fill(${JSON.stringify(selector)}, ${JSON.stringify(value)})`);
  }
}

// Injected into every document: click-by-text and React-friendly input filling.
const HELPERS = `
  window.__find = (sel, text) => {
    const els = [...document.querySelectorAll(sel)].filter((e) => e.offsetParent !== null || e === document.activeElement);
    return text == null ? els[0] : els.find((e) => (e.innerText || e.textContent || "").trim().includes(text));
  };
  window.__click = (sel, text) => {
    const el = __find(sel, text);
    if (!el) throw new Error("No element for " + sel + (text ? " with text " + text : ""));
    if (el.disabled) throw new Error("Element is disabled: " + sel + " " + (text || ""));
    el.click();
    return true;
  };
  window.__fill = (sel, value) => {
    const el = document.querySelector(sel);
    if (!el) throw new Error("No input for " + sel);
    const proto = el.tagName === "TEXTAREA" ? HTMLTextAreaElement.prototype : el.tagName === "SELECT" ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(proto, "value").set.call(el, value);
    el.dispatchEvent(new Event(el.tagName === "SELECT" ? "change" : "input", { bubbles: true }));
    return true;
  };
`;

async function launchBrowser() {
  const exe = BROWSER_CANDIDATES.find((p) => existsSync(p));
  if (!exe) throw new Error("No Chrome/Edge found. Set BROWSER=/path/to/chrome.");
  const profile = mkdtempSync(join(tmpdir(), "shopsphere-e2e-"));
  const proc = spawn(
    exe,
    [
      "--headless=new",
      `--remote-debugging-port=${PORT}`,
      `--user-data-dir=${profile}`,
      "--no-first-run",
      "--no-default-browser-check",
      "--disable-gpu",
      "--window-size=1366,900",
      "about:blank",
    ],
    { stdio: "ignore" }
  );
  let target;
  for (let i = 0; i < 60 && !target; i++) {
    await sleep(250);
    try {
      const list = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
      target = list.find((t) => t.type === "page");
    } catch {
      /* not up yet */
    }
  }
  if (!target) throw new Error("Browser did not expose a page target.");
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    ws.addEventListener("open", resolve, { once: true });
    ws.addEventListener("error", reject, { once: true });
  });
  const page = new Page(ws);
  const close = () => {
    try {
      ws.close();
    } catch {}
    proc.kill();
    // Best-effort: on Windows the browser can hold the profile folder briefly after exit.
    setTimeout(() => {
      try {
        rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 300 });
      } catch {
        /* leftover temp profile is harmless */
      }
    }, 1500);
  };
  return { page, close, exe };
}

// ---------------------------------------------------------------------------
// Test runner
// ---------------------------------------------------------------------------
const results = [];
async function step(name, fn) {
  const started = Date.now();
  try {
    await fn();
    results.push({ name, ok: true });
    console.log(`  ✔ ${name} (${Date.now() - started}ms)`);
  } catch (error) {
    results.push({ name, ok: false, error });
    console.log(`  ✖ ${name}\n      ${error.message}`);
    throw error;
  }
}

async function apiAs(page, method, path) {
  const token = await page.eval(`JSON.parse(localStorage.getItem("shopsphere_session") || "null")?.accessToken ?? null`);
  const res = await fetch(GATEWAY + path, { method, headers: { Authorization: `Bearer ${token}` } });
  return { status: res.status, data: res.status === 204 ? null : await res.json() };
}

/** If a run fails mid-way, make sure the product it created doesn't linger in the catalog. */
async function removeLeftoverProduct(sku) {
  try {
    const found = await (await fetch(`${GATEWAY}/catalog/products?pageSize=100&searchTerm=${encodeURIComponent(sku)}`)).json();
    if (!found.items?.length) return;
    const login = await fetch(`${GATEWAY}/identity/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: "admin@shopsphere.local", password: "AdminPassword123!" }),
    });
    const { accessToken } = await login.json();
    for (const p of found.items) {
      const headers = { Authorization: `Bearer ${accessToken}` };
      await fetch(`${GATEWAY}/inventory/items/${p.id}`, { method: "DELETE", headers });
      await fetch(`${GATEWAY}/catalog/products/${p.id}`, { method: "DELETE", headers });
    }
    console.log(`  (cleaned up leftover test product ${sku})`);
  } catch {
    /* best-effort cleanup */
  }
}

async function main() {
  console.log(`ShopSphere browser E2E — storefront ${BASE_URL}, gateway ${GATEWAY}`);
  const { page, close, exe } = await launchBrowser();
  console.log(`Browser: ${exe}\n`);

  await page.send("Page.enable");
  await page.send("Runtime.enable");
  await page.send("Log.enable");
  await page.send("Page.addScriptToEvaluateOnNewDocument", { source: HELPERS });
  page.on("Runtime.exceptionThrown", (p) =>
    page.errors.push(`Uncaught: ${p.exceptionDetails.exception?.description ?? p.exceptionDetails.text}`)
  );
  page.on("Runtime.consoleAPICalled", (p) => {
    if (p.type === "error" || p.type === "assert") {
      page.errors.push(`console.${p.type}: ${p.args.map((a) => a.value ?? a.description).join(" ")}`);
    }
  });
  // Network-level failures (e.g. unreachable product image hosts, expected 404s) are
  // reported by the browser as Log entries with source "network"; anything else is a bug.
  page.on("Log.entryAdded", ({ entry }) => {
    if (entry.level === "error" && entry.source !== "network") page.errors.push(`log: ${entry.text}`);
  });
  page.on("Page.javascriptDialogOpening", () => page.send("Page.handleJavaScriptDialog", { accept: true }));

  const stamp = Date.now().toString(36);
  const shopper = { first: "Grace", last: "Hopper", email: `e2e-${stamp}@shopsphere.test`, password: "E2e!Passw0rd" };
  const newProduct = { name: `E2E Desk Lamp ${stamp}`, sku: `E2E-LAMP-${stamp}`.toUpperCase(), price: "59.90" };
  let firstProductName;

  try {
    console.log("Pages (HTTP)");
    for (const route of ["/", "/login", "/register", "/orders", "/admin"]) {
      await step(`GET ${route} → 200`, async () => {
        const res = await fetch(BASE_URL + route);
        assert.equal(res.status, 200);
      });
    }

    console.log("\nGuest storefront");
    await step("home renders hero, categories and live product cards", async () => {
      await page.goto("/");
      await page.waitForText("Upgrade your desk");
      await page.waitFor(`document.querySelectorAll("#catalog article").length > 0`, { label: "product cards" });
      await page.waitFor(`/In stock: \\d+ units|Only \\d+ left|Out of stock/.test(document.querySelector("#catalog").innerText)`, {
        label: "inventory status on cards",
      });
      assert.ok(await page.hasText("Smart Home"), "category navigation rendered");
      firstProductName = await page.eval(`document.querySelector("#catalog article h3").innerText`);
    });

    await step("broken image URLs fall back to a placeholder", async () => {
      await page.waitFor(`document.querySelectorAll("#catalog article [role=img]").length > 0`, { label: "image placeholders" });
    });

    await step("navbar search filters the catalog instantly", async () => {
      await page.fill(`input[type=search]`, "mouse");
      await page.waitFor(`location.search.includes("q=mouse")`, { label: "q in URL" });
      await page.waitFor(
        `[...document.querySelectorAll("#catalog article h3")].length > 0 && [...document.querySelectorAll("#catalog article")].every(a => a.innerText.toLowerCase().includes("mouse"))`,
        { label: "only matching products" }
      );
      await page.fill(`input[type=search]`, "zzzz-no-match");
      await page.waitForText("No products found");
      await page.click("button", "Clear filters");
      await page.waitFor(`document.querySelectorAll("#catalog article").length > 1`, { label: "filters cleared" });
    });

    await step("category tabs filter by category", async () => {
      await page.click(`#catalog [role=tab]`, "Peripherals");
      await page.waitFor(`location.search.includes("category=Peripherals")`);
      await page.waitFor(
        `[...document.querySelectorAll("#catalog article")].every(a => a.innerText.includes("PERIPHERALS") || a.innerText.includes("Peripherals"))`,
        { label: "only Peripherals cards" }
      );
      await page.click(`#catalog [role=tab]`, "All");
    });

    await step("guest can add to cart; drawer asks them to sign in", async () => {
      await page.waitFor(`document.querySelectorAll("#catalog article").length > 1`);
      await page.click(`#catalog article button`, "Add to cart");
      await page.waitFor(`document.querySelector('[aria-label="Shopping cart"]').getAttribute("aria-hidden") === "false"`, {
        label: "cart drawer open",
      });
      await page.waitForText("Sign in to check out");
      assert.ok(await page.eval(`document.querySelector('[aria-label^="Open cart, 1 item"]') !== null`), "badge shows 1");
      await page.click(`[aria-label="Close cart"]`);
    });

    await step("guests are redirected from /orders and /admin to sign in", async () => {
      await page.goto("/orders");
      await page.waitFor(`location.pathname === "/login" && location.search.includes("next=%2Forders")`, { label: "redirect" });
      await page.goto("/admin");
      await page.waitFor(`location.pathname === "/login" && location.search.includes("next=%2Fadmin")`, { label: "redirect" });
    });

    console.log("\nRegistration & cart sync");
    await step("register form validates input before submitting", async () => {
      await page.goto("/register");
      await page.waitForText("Create your account");
      await page.click("button[type=submit]");
      await page.waitForText("First name is required.");
      await page.fill(`input[name=password]`, "short");
      assert.ok(await page.hasText("Password doesn't meet the requirements"));
    });

    await step("registering signs the new customer in", async () => {
      await page.fill(`input[name=firstName]`, shopper.first);
      await page.fill(`input[name=lastName]`, shopper.last);
      await page.fill(`input[name=email]`, shopper.email);
      await page.fill(`input[name=password]`, shopper.password);
      await page.fill(`input[name=confirm]`, shopper.password);
      await page.click("button[type=submit]");
      await page.waitFor(`location.pathname === "/"`, { label: "redirect home" });
      await page.waitFor(`document.querySelector("header").innerText.includes("Grace")`, { label: "navbar shows user" });
      assert.ok(await page.eval(`document.querySelector("header").innerText.includes("Customer")`));
    });

    await step("guest cart is merged into the account's Redis basket", async () => {
      await page.waitFor(`document.querySelector('[aria-label^="Open cart, 1 item"]') !== null`, { label: "badge 1" });
      let basket;
      for (let i = 0; i < 20; i++) {
        basket = await apiAs(page, "GET", "/basket");
        if (basket.data?.items?.length === 1) break;
        await sleep(300);
      }
      assert.equal(basket.data.items.length, 1);
      assert.equal(basket.data.items[0].quantity, 1);
    });

    await step("changing quantity in the drawer syncs to Redis", async () => {
      await page.click(`[aria-label^="Open cart"]`);
      await page.click(`button[aria-label^="Increase quantity"]`);
      await page.waitFor(`document.querySelector('[aria-label^="Open cart, 2 items"]') !== null`, { label: "badge 2" });
      let basket;
      for (let i = 0; i < 20; i++) {
        basket = await apiAs(page, "GET", "/basket");
        if (basket.data?.items?.[0]?.quantity === 2) break;
        await sleep(300);
      }
      assert.equal(basket.data.items[0].quantity, 2);
    });

    console.log("\nCheckout");
    await step("checkout validates the shipping address", async () => {
      await page.click("aside button", "Checkout");
      await page.waitFor(`document.querySelector('[aria-labelledby="checkout-title"]') !== null`, { label: "checkout modal" });
      await page.click(`[aria-labelledby="checkout-title"] button[type=submit]`);
      await page.waitForText("Street address is required.");
    });

    await step("declined test card shows 'Payment declined' and keeps the cart", async () => {
      await page.fill(`input[name=street]`, "1 Infinite Loop");
      await page.fill(`input[name=city]`, "Cupertino");
      await page.fill(`input[name=state]`, "CA");
      await page.fill(`input[name=zipCode]`, "95014");
      await page.click(`input[type=radio][value=decline]`);
      await page.click(`[aria-labelledby="checkout-title"] button[type=submit]`);
      await page.waitForText("Payment declined", 30000);
      assert.ok(await page.hasText("Card Declined"));
      assert.ok(await page.eval(`document.querySelector('[aria-label^="Open cart, 2 items"]') !== null`), "cart kept");
    });

    await step("retrying with the standard card shows 'Order placed' receipt", async () => {
      await page.click("button", "Try a different card");
      await page.waitFor(`document.querySelector("input[type=radio][value=standard]").checked`, { label: "standard card selected" });
      assert.equal(await page.eval(`document.querySelector("input[name=street]").value`), "1 Infinite Loop", "address kept");
      await page.click(`[aria-labelledby="checkout-title"] button[type=submit]`);
      await page.waitForText("Order placed", 30000);
      assert.ok(await page.eval(`/#[0-9A-F]{8}/.test(document.body.innerText)`), "order number shown");
      await page.waitFor(`document.querySelector('[aria-label="Open cart, 0 items"]') !== null`, { label: "cart cleared" });
      const basket = await apiAs(page, "GET", "/basket");
      assert.equal(basket.data.items.length, 0, "Redis basket cleared");
    });

    await step("order history lists both orders with customer-facing statuses", async () => {
      await page.click("a", "View my orders");
      await page.waitFor(`location.pathname === "/orders"`);
      await page.waitForText("2 orders");
      const text = await page.text();
      assert.ok(text.includes("Order Placed"), "approved order badge");
      assert.ok(text.includes("Payment Declined"), "declined order badge");
      assert.ok(text.includes("Cupertino"), "shipping address");
      assert.ok(!/saga|rabbit|compensat|AwaitingPayment|StockReserved/i.test(text), "no internal jargon");
    });

    await step("customers see 'Admin access required' on /admin", async () => {
      await page.goto("/admin");
      await page.waitForText("Admin access required");
    });

    console.log("\nAdmin");
    await step("sign out, then sign in with the admin demo preset", async () => {
      await page.click("header button", "Sign out");
      await page.waitFor(`document.querySelector("header").innerText.includes("Sign in")`, { label: "signed out" });
      await page.goto("/login?next=%2Fadmin");
      await page.waitForText("Welcome back");
      await page.click("button", "Fill admin credentials");
      assert.equal(await page.eval(`document.querySelector("input[name=email]").value`), "admin@shopsphere.local");
      await page.click("form button[type=submit]");
      await page.waitFor(`location.pathname === "/admin"`, { label: "redirect to admin" });
      await page.waitForText("Store management");
    });

    await step("admin console lists products with stock levels", async () => {
      await page.waitFor(`document.querySelectorAll("tbody tr").length > 0 && document.body.innerText.includes("available")`, {
        label: "product table",
      });
      assert.ok(await page.hasText(firstProductName), "catalog product listed");
    });

    await step("add-product form validates, then creates a product with 50 units of stock", async () => {
      await page.click("form button[type=submit]", "Add product");
      await page.waitForText("Name is required.");
      assert.equal(await page.eval(`document.querySelector("form input[name=initialStock]").value`), "50", "default initial stock");
      await page.fill(`form input[name=name]`, newProduct.name);
      await page.fill(`form textarea[name=description]`, "Warm, dimmable LED desk lamp created by the E2E suite.");
      await page.fill(`form input[name=sku]`, newProduct.sku);
      await page.fill(`form input[name=price]`, newProduct.price);
      await page.fill(`form select[name=category]`, "Smart Home");
      await page.click("form button[type=submit]", "Add product");
      await page.waitForText("with 50 units in stock");
      const row = `[...document.querySelectorAll("tbody tr")].find(r => r.innerText.includes(${JSON.stringify(newProduct.name)}))`;
      await page.waitFor(`${row}?.innerText.includes("50 available")`, { label: "row shows 50 available" });
      assert.ok(await page.eval(`!!document.querySelector('button[aria-label="Add for ${newProduct.name}"]')`), "stock controls present");
    });

    await step("admin adds and removes stock for the new product", async () => {
      const row = `[...document.querySelectorAll("tbody tr")].find(r => r.innerText.includes(${JSON.stringify(newProduct.name)}))`;
      await page.fill(`input[aria-label="Units for ${newProduct.name}"]`, "5");
      await page.click(`button[aria-label="Add for ${newProduct.name}"]`);
      await page.waitFor(`${row}?.innerText.includes("55 available")`, { label: "55 available" });
      await page.fill(`input[aria-label="Units for ${newProduct.name}"]`, "3");
      await page.click(`button[aria-label="Remove for ${newProduct.name}"]`);
      await page.waitFor(`${row}?.innerText.includes("52 available")`, { label: "52 available" });
    });

    await step("admin edits the product (name and price)", async () => {
      await page.click(`button[aria-label="Edit ${newProduct.name}"]`);
      await page.waitFor(`document.querySelector('[aria-labelledby="edit-product-title"]') !== null`, { label: "edit dialog" });
      newProduct.name = `${newProduct.name} Pro`;
      await page.fill(`[aria-labelledby="edit-product-title"] input[name=name]`, newProduct.name);
      await page.fill(`[aria-labelledby="edit-product-title"] input[name=price]`, "64.90");
      await page.click(`[aria-labelledby="edit-product-title"] button[type=submit]`, "Save changes");
      await page.waitForText("was updated");
      assert.ok(await page.hasText("$64.90"));
    });

    await step("the new product is buyable in the storefront with live stock", async () => {
      await page.goto(`/?q=${encodeURIComponent(newProduct.sku)}`);
      await page.waitFor(`document.querySelectorAll("#catalog article").length === 1`, { label: "one search result" });
      assert.ok(await page.hasText(newProduct.name));
      assert.ok(await page.hasText("$64.90"));
      await page.waitForText("In stock: 52 units");
      assert.ok(await page.eval(`!document.querySelector("#catalog article button").disabled`), "Add to cart enabled");
    });

    await step("admin deletes the product (and its stock record)", async () => {
      await page.goto("/admin");
      await page.waitForText(newProduct.name);
      await page.click(`button[aria-label="Delete ${newProduct.name}"]`);
      await page.waitForText("was removed from the catalog");
      const res = await fetch(`${GATEWAY}/catalog/products?pageSize=100&searchTerm=${encodeURIComponent(newProduct.sku)}`);
      assert.equal((await res.json()).totalCount, 0);
      assert.equal((await fetch(`${GATEWAY}/inventory/${encodeURIComponent(newProduct.sku)}`)).status, 404, "stock record removed");
    });

    await step("no uncaught exceptions, console errors or hydration errors", async () => {
      assert.deepEqual(page.errors, []);
    });
  } finally {
    close();
    await removeLeftoverProduct(newProduct.sku);
  }

  const failed = results.filter((r) => !r.ok).length;
  console.log(`\n${results.length - failed}/${results.length} browser checks passed`);
  if (failed) process.exitCode = 1;
}

main().catch((error) => {
  console.error(`\nE2E FAILED: ${error.message}`);
  process.exit(1);
});
