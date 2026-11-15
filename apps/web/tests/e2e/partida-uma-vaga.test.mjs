import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import test from "node:test";
import { chromium } from "playwright";

const webOrigin = "http://localhost:3000";
const keycloakOrigin = "http://localhost:8080";
const connectedPath = "/peladas/conectadas";
const waitTimeout = 15_000;

async function signIn(browser, username, password) {
  const context = await browser.newContext({
    baseURL: webOrigin,
    locale: "pt-BR",
    timezoneId: "America/Sao_Paulo",
  });
  context.setDefaultTimeout(waitTimeout);

  const page = await context.newPage();
  await page.goto(connectedPath);
  await Promise.all([
    page.waitForURL((url) => url.origin === keycloakOrigin && url.pathname.startsWith("/realms/applada/")),
    page.getByRole("button", { name: "Entrar com conta de teste", exact: true }).click(),
  ]);
  await page.locator("#username").fill(username);
  await page.locator("#password").focus();
  try {
    await page.keyboard.insertText(password);
  } catch {
    throw new Error(`Não foi possível digitar a senha da conta de teste ${username}.`);
  }
  await page.locator("#kc-login").click();

  const signedIn = page.getByRole("button", { name: "Sair da sessão", exact: true });
  const keycloakNotice = page.locator("#kc-passwd-update-form, #input-error");
  await signedIn.or(keycloakNotice).first().waitFor({ state: "visible" });
  assert.ok(await signedIn.isVisible(), `Prepare a conta local ${username} e confira a senha dos testes.`);
  assert.equal(new URL(page.url()).origin, webOrigin);

  const session = await context.request.get("/api/local/session", { timeout: waitTimeout });
  assert.equal(session.status(), 200, `Sessão do Applada para ${username}`);
  const { userId } = await session.json();
  assert.ok(userId, `Identidade do Applada para ${username}`);
  return { page, userId };
}

async function clickAndCheck(page, button, method, path, status) {
  const [response] = await Promise.all([
    page.waitForResponse((candidate) => candidate.url() === webOrigin + path && candidate.request().method() === method),
    button.click(),
  ]);
  assert.equal(response.status(), status, `${method} ${path}`);
  return response;
}

test("uma vaga: entra na fila, promove o próximo e revoga acesso de quem sai", { timeout: 120_000 }, async (t) => {
  const password = process.env.APPLADA_E2E_TEST_PASSWORD;
  assert.ok(password, "Defina APPLADA_E2E_TEST_PASSWORD com a senha das três contas locais de teste.");

  const browserEnv = { ...process.env };
  delete browserEnv.APPLADA_E2E_TEST_PASSWORD;
  const browser = await chromium.launch({ channel: "chrome", headless: true, env: browserEnv });
  let organizer;
  let matchId;

  t.after(async () => {
    try {
      if (matchId) {
        const response = await organizer.page.request.post(`/api/local/matches/${matchId}/cancellation`, {
          headers: { Origin: webOrigin },
          timeout: waitTimeout,
        });
        assert.equal(response.status(), 200, `Cancelamento da partida de teste ${matchId}`);
        assert.equal((await response.json()).status, "cancelled");
      }
    } finally {
      await browser.close();
    }
  });

  organizer = await signIn(browser, "jogador3.dev", password);
  const first = await signIn(browser, "jogador1.dev", password);
  const second = await signIn(browser, "jogador2.dev", password);
  assert.equal(new Set([organizer.userId, first.userId, second.userId]).size, 3);

  const title = `E2E vaga única ${randomUUID()}`;
  t.diagnostic(`Título reservado para o teste ${title}`);
  const form = organizer.page.getByRole("region", { name: "Organizar partida", exact: true });
  const startsAt = await organizer.page.evaluate(() => {
    const future = new Date(Date.now() + 24 * 60 * 60_000);
    return new Date(future.getTime() - future.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
  });
  await form.getByLabel("Nome da partida").fill(title);
  await form.getByLabel("Número de vagas").fill("1");
  await form.getByLabel("Início no horário local do seu dispositivo").fill(startsAt);

  const created = await clickAndCheck(
    organizer.page, form.getByRole("button", { name: "Criar partida", exact: true }), "POST", "/api/local/matches", 201,
  );
  const match = await created.json();
  assert.match(match.id, /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i);
  matchId = match.id;
  assert.equal(match.capacity, 1);

  const details = `/api/local/matches/${matchId}`;
  const registrations = details + "/registrations";
  const matchPage = `${connectedPath}?partida=${matchId}`;

  await first.page.goto(matchPage);
  const firstCard = first.page.getByRole("region", { name: title, exact: true });
  const joined = await clickAndCheck(
    first.page, firstCard.getByRole("button", { name: "Confirmar presença", exact: true }), "POST", registrations, 201,
  );
  assert.equal((await joined.json()).status, "confirmed");
  await firstCard.getByText("Confirmado", { exact: true }).waitFor({ state: "visible" });

  await second.page.goto(matchPage);
  const secondCard = second.page.getByRole("region", { name: title, exact: true });
  const queued = await clickAndCheck(
    second.page, secondCard.getByRole("button", { name: "Entrar na lista de espera", exact: true }), "POST", registrations, 201,
  );
  const queueEntry = await queued.json();
  assert.equal(queueEntry.status, "waitlisted");
  assert.equal(queueEntry.position, 1, "Posição do segundo jogador na fila");
  await secondCard.getByText("1º na lista de espera", { exact: true }).waitFor({ state: "visible" });

  const firstListButton = firstCard.getByRole("button", { name: "Ver participantes", exact: true });
  const visibleList = firstCard.getByRole("heading", { name: "Participantes", exact: true });
  const list = await clickAndCheck(first.page, firstListButton, "GET", registrations, 200);
  assert.deepEqual((await list.json()).items.map(({ userId, status }) => [userId, status]), [
    [first.userId, "confirmed"],
    [second.userId, "waitlisted"],
  ]);
  await visibleList.waitFor({ state: "visible" });

  await clickAndCheck(
    first.page, firstCard.getByRole("button", { name: "Retirar inscrição", exact: true }), "DELETE", registrations + "/me", 204,
  );
  await firstListButton.waitFor({ state: "hidden" });
  await visibleList.waitFor({ state: "hidden" });

  const denied = await first.page.request.get(registrations, { timeout: waitTimeout });
  assert.equal(denied.status(), 403, "O jogador que saiu não pode consultar os participantes");
  assert.equal((await denied.json()).type, "urn:applada:problem:participants-forbidden");

  const refreshed = await clickAndCheck(
    second.page, secondCard.getByRole("button", { name: "Atualizar partida", exact: true }), "GET", details, 200,
  );
  const afterPromotion = await refreshed.json();
  assert.equal(afterPromotion.confirmedCount, 1);
  assert.equal(afterPromotion.waitlistCount, 0);
  assert.equal(afterPromotion.myRegistration.status, "confirmed");
  await secondCard.getByText("Confirmado", { exact: true }).waitFor({ state: "visible" });

  const finalList = await clickAndCheck(
    second.page, secondCard.getByRole("button", { name: "Ver participantes", exact: true }), "GET", registrations, 200,
  );
  assert.deepEqual((await finalList.json()).items.map(({ userId, status }) => [userId, status]), [
    [second.userId, "confirmed"],
  ]);
});
