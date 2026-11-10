import { test } from "node:test";
import assert from "node:assert/strict";
import { DEMO, call, login, uniqueSuffix } from "./gateway.mjs";

test("Auth lifecycle: register → login → token → profile → refresh", async (t) => {
  const email = `it-auth-${uniqueSuffix()}@shopsphere.test`;
  const password = "Str0ng!Passw0rd";
  let tokens;

  await t.test("rejects a weak password with a validation error", async () => {
    const res = await call("POST", "/identity/register", {
      body: { email, password: "weak", firstName: "A", lastName: "B" },
    });
    assert.equal(res.status, 400);
    assert.ok(res.data.errors, "validation problem details include field errors");
  });

  await t.test("registers a new customer (201)", async () => {
    const res = await call("POST", "/identity/register", {
      body: { email, password, firstName: "Ada", lastName: "Lovelace" },
    });
    assert.equal(res.status, 201);
    assert.equal(res.data.email, email);
    assert.equal(res.data.firstName, "Ada");
  });

  await t.test("refuses a duplicate registration (409)", async () => {
    const res = await call("POST", "/identity/register", {
      body: { email, password, firstName: "Ada", lastName: "Lovelace" },
    });
    assert.equal(res.status, 409);
    assert.match(res.data.detail, /already registered/i);
  });

  await t.test("rejects wrong credentials (401)", async () => {
    const res = await call("POST", "/identity/login", { body: { email, password: "Wr0ng!Password" } });
    assert.equal(res.status, 401);
    assert.equal(res.data.detail, "Invalid credentials.");
  });

  await t.test("logs in and issues access + refresh tokens", async () => {
    tokens = await login(email, password);
    assert.equal(typeof tokens.accessToken, "string");
    assert.equal(tokens.accessToken.split(".").length, 3, "access token is a JWT");
    assert.ok(tokens.refreshToken);
    assert.ok(tokens.expiresIn > 0);
  });

  await t.test("profile (/identity/me) resolves the Customer role", async () => {
    const res = await call("GET", "/identity/me", { token: tokens.accessToken });
    assert.equal(res.status, 200);
    assert.equal(res.data.email, email);
    assert.deepEqual(res.data.roles, ["Customer"]);
  });

  await t.test("refresh token rotates into a working access token", async () => {
    const res = await call("POST", "/identity/refresh", { body: { refreshToken: tokens.refreshToken } });
    assert.equal(res.status, 200);
    assert.notEqual(res.data.refreshToken, tokens.refreshToken, "refresh token is rotated");
    const me = await call("GET", "/identity/me", { token: res.data.accessToken });
    assert.equal(me.status, 200);
  });

  await t.test("demo admin account resolves an admin role", async () => {
    const admin = await login(DEMO.admin.email, DEMO.admin.password);
    const me = await call("GET", "/identity/me", { token: admin.accessToken });
    assert.ok(me.data.roles.some((r) => r === "Admin" || r === "Administrator"));
  });

  await t.test("protected endpoints reject anonymous calls (401)", async () => {
    const res = await call("GET", "/identity/me");
    assert.equal(res.status, 401);
  });
});
