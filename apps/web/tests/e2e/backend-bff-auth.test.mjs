import assert from "node:assert/strict";
import test from "node:test";
import { chromium } from "playwright";

const webOrigin = process.env.APPLADA_E2E_WEB_ORIGIN ?? "http://localhost:3000";
const keycloakOrigin = process.env.APPLADA_E2E_KEYCLOAK_ORIGIN ?? "http://localhost:8080";

test("Auth.js BFF usa a sessão do servidor sem entregar tokens ao browser", { timeout: 60_000 }, async (t) => {
  const password = process.env.APPLADA_E2E_TEST_PASSWORD;
  assert.ok(password, "Defina APPLADA_E2E_TEST_PASSWORD com a senha da conta local de teste.");

  const browser = await chromium.launch({ channel: "chrome", headless: true });
  t.after(() => browser.close());

  const context = await browser.newContext({
    baseURL: webOrigin,
    locale: "pt-BR",
  });
  const page = await context.newPage();
  await page.goto("/login");
  await Promise.all([
    page.waitForURL((url) => url.origin === keycloakOrigin && url.pathname.endsWith("/protocol/openid-connect/auth")),
    page.getByRole("button", { name: "Entrar com minha conta", exact: true }).click(),
  ]);
  await page.locator("#username").fill("jogador3.dev");
  await page.locator("#password").focus();
  await page.keyboard.insertText(password);
  await Promise.all([
    page.waitForURL((url) => url.origin === webOrigin && url.pathname === "/"),
    page.locator("#kc-login").click(),
  ]);

  const identityResponse = await page.request.get("/api/backend/session");
  assert.equal(identityResponse.status(), 200);
  assert.match(identityResponse.headers()["cache-control"] ?? "", /private, no-store/i);
  const identity = await identityResponse.json();
  assert.match(identity.userId ?? "", /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i);
  assert.equal(JSON.stringify(identity).includes("keycloakAccessToken"), false);
  assert.equal(await page.evaluate(() => document.cookie.includes("authjs.session-token")), false);

  const profile = await page.request.get("/api/backend/profile");
  assert.equal(profile.status(), 200);
  const profileBody = await profile.json();
  assert.equal(profileBody.displayName === null || typeof profileBody.displayName === "string", true);

  const matches = await page.request.get("/api/backend/mine?role=organizer");
  assert.equal(matches.status(), 200);
  assert.match(matches.headers()["cache-control"] ?? "", /private, no-store/i);
  assert.equal((await matches.json()).items instanceof Array, true);

  const unsupported = await page.request.get("/api/backend/administration/users");
  assert.equal(unsupported.status(), 404);

  await context.close();
});
