import assert from "node:assert/strict";
import { chromium } from "playwright";

export const webOrigin = "http://localhost:3000";
export const keycloakOrigin = "http://localhost:8080";
export const waitTimeout = 15_000;

// Chrome sem a senha no ambiente do navegador.
export function launchChrome() {
  const browserEnv = { ...process.env };
  delete browserEnv.APPLADA_E2E_TEST_PASSWORD;
  return chromium.launch({ channel: "chrome", headless: true, env: browserEnv });
}

export async function newContext(browser) {
  const context = await browser.newContext({
    baseURL: webOrigin,
    locale: "pt-BR",
    timezoneId: "America/Sao_Paulo",
  });
  context.setDefaultTimeout(waitTimeout);
  return context;
}

export function isKeycloak(url) {
  return url.origin === keycloakOrigin && url.pathname.startsWith("/realms/applada/");
}

// Preenche o formulário hospedado do Keycloak e espera o retorno ao site (ou um aviso do Keycloak).
export async function fillKeycloak(page, username, password) {
  await page.locator("#username").fill(username);
  await page.locator("#password").focus();
  try {
    await page.keyboard.insertText(password);
  } catch {
    throw new Error(`Não foi possível digitar a senha da conta de teste ${username}.`);
  }
  await page.locator("#kc-login").click();

  const keycloakNotice = page.locator("#kc-passwd-update-form, #input-error");
  await Promise.race([
    page.waitForURL((url) => url.origin === webOrigin),
    keycloakNotice.first().waitFor({ state: "visible" }),
  ]);
  assert.equal(new URL(page.url()).origin, webOrigin, `Prepare a conta local ${username} e confira a senha dos testes.`);
}

// Abre /login, entra pelo Keycloak e termina em `landing` com a sessão conferida em GET /api/app/me.
export async function signIn(browser, username, password, landing = "/") {
  const context = await newContext(browser);
  const page = await context.newPage();
  await page.goto("/login");
  await Promise.all([
    page.waitForURL((url) => isKeycloak(url)),
    page.getByRole("button", { name: "Entrar com minha conta", exact: true }).click(),
  ]);
  await fillKeycloak(page, username, password);

  const session = await context.request.get("/api/app/me", { timeout: waitTimeout });
  assert.equal(session.status(), 200, `Sessão do Applada para ${username}`);
  const { userId } = await session.json();
  assert.ok(userId, `Identidade do Applada para ${username}`);
  await page.goto(landing);
  return { context, page, userId };
}

export function requirePassword() {
  const password = process.env.APPLADA_E2E_TEST_PASSWORD;
  assert.ok(password, "Defina APPLADA_E2E_TEST_PASSWORD com a senha das três contas locais de teste.");
  return password;
}

export async function runCleanups(cleanups, browser) {
  try {
    // Ordem inversa: o que foi criado depois sai primeiro.
    const failures = [];
    for (const cleanup of [...cleanups].reverse()) {
      try {
        await cleanup();
      } catch (failure) {
        failures.push(failure);
      }
    }
    if (failures.length > 0) {
      throw new AggregateError(failures, "Falha na limpeza dos dados do E2E.");
    }
  } finally {
    await browser.close();
  }
}
