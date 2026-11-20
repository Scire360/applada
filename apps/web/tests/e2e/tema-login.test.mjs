import assert from "node:assert/strict";
import { createHash, randomBytes } from "node:crypto";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { chromium } from "playwright";

const testPort = Number(process.env.APPLADA_AUTH_TEST_PORT ?? "18080");
assert.ok(Number.isInteger(testPort) && testPort > 0 && testPort <= 65535);
const keycloak = "http://localhost:" + testPort;
const brave = process.env.APPLADA_TEST_BRAVE_PATH
  ?? "C:/Program Files/BraveSoftware/Brave-Browser/Application/brave.exe";
const viewports = [
  [320, 568], [390, 844], [568, 320], [768, 1024], [1024, 600], [1366, 768], [2560, 1440],
].map(([width, height]) => ({ width, height }));

async function checkLayout(page, form, submit) {
  for (const viewport of viewports) {
    await page.setViewportSize(viewport);
    const metrics = await form.evaluate(async (element) => {
      await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      const { left, right } = element.getBoundingClientRect();
      const { clientWidth, scrollWidth } = document.documentElement;
      const container = element.closest(".pf-v5-c-login__container");
      const showPitch = getComputedStyle(container, "::before").content === '""';
      return { left, right, clientWidth, scrollWidth, showPitch };
    });
    const context = form + " em " + viewport.width + "×" + viewport.height;
    assert.ok(metrics.left >= 0 && metrics.right <= metrics.clientWidth, context);
    assert.ok(metrics.scrollWidth <= metrics.clientWidth, context);
    assert.ok((await submit.boundingBox())?.height >= 44, context);
    assert.equal(metrics.showPitch, viewport.width >= 1366, context);
  }
}

test("login e troca de senha do Applada em português e responsivos no Brave", { timeout: 60_000 }, async (t) => {
  const realm = JSON.parse(await readFile(new URL("../../../../infra/keycloak/applada-dev-realm.json", import.meta.url), "utf8"));
  const organizer = realm.users.find((user) => user.username === "organizador.dev");
  const authorize = new URL("/realms/applada/protocol/openid-connect/auth", keycloak);
  const verifier = randomBytes(32).toString("base64url");
  authorize.search = new URLSearchParams({
    client_id: "applada-dev-web",
    redirect_uri: "http://localhost:3000/api/local/callback",
    response_type: "code",
    scope: "openid",
    state: randomBytes(16).toString("base64url"),
    code_challenge: createHash("sha256").update(verifier).digest("base64url"),
    code_challenge_method: "S256",
  }).toString();

  const browser = await chromium.launch({ executablePath: brave, headless: true });
  try {
    const page = await browser.newPage({ locale: "en-US", colorScheme: "dark", viewport: viewports[0] });
    assert.equal((await page.goto(authorize.href))?.status(), 200);
    assert.equal(await page.locator("html").getAttribute("lang"), "pt-BR");
    await page.getByRole("heading", { name: "Entrar na sua conta", exact: true }).waitFor();
    const themeStyle = page.locator('link[rel="stylesheet"][href$="/login/applada/css/applada.css"]');
    const cssUrl = await themeStyle.getAttribute("href");
    assert.ok(cssUrl && (await page.request.get(new URL(cssUrl, keycloak).href)).ok());
    const login = page.locator("#kc-form-login");
    await checkLayout(page, login, login.getByRole("button", { name: "Entrar", exact: true }));

    const username = login.getByLabel("Nome de usuário", { exact: true });
    await username.fill(organizer.username);
    await login.getByLabel("Senha", { exact: true }).fill("senha-incorreta-do-teste");
    await login.getByRole("button", { name: "Entrar", exact: true }).click();
    await page.getByText("Nome de usuário ou senha inválida.", { exact: true }).waitFor();
    await username.fill(organizer.username);
    await login.getByLabel("Senha", { exact: true }).fill(organizer.credentials[0].value);
    await login.getByRole("button", { name: "Entrar", exact: true }).click();
    await page.getByRole("heading", { name: "Atualizar senha", exact: true }).waitFor();

    const update = page.locator("#kc-passwd-update-form");
    await checkLayout(page, update, update.getByRole("button", { name: "Salvar", exact: true }));
    await update.getByLabel("Nova senha", { exact: true }).waitFor();
    const show = update.getByRole("button", { name: "Mostrar senha", exact: true }).first();
    await show.click();
    assert.equal(await page.locator("#password-new").getAttribute("type"), "text");
    await update.getByRole("button", { name: "Ocultar senha", exact: true }).click();
    assert.equal(await page.locator("#password-new").getAttribute("type"), "password");
    t.diagnostic(viewports.length + " tamanhos por formulário, erro de credenciais e controle de visibilidade");
  } finally {
    await browser.close();
  }
});
