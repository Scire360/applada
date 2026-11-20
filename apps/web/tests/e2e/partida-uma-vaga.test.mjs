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

async function checkAvatar(browser, first, second, password, cleanups) {
  const path = "/api/local/avatar";
  const initial = await second.page.request.get(path, { timeout: waitTimeout });
  assert.equal(initial.status(), 204, "O jogador2.dev precisa começar sem foto para o teste de Storage.");

  const account = second.page.getByRole("banner").getByRole("button", { name: "Abrir conta", exact: true });
  const dialog = second.page.getByRole("dialog", { name: "Sua conta" });
  await account.click();
  const picker = dialog.getByLabel("Escolher foto", { exact: true });
  await picker.setInputFiles({ name: "grande.png", mimeType: "image/png", buffer: Buffer.alloc(1_000_001) });
  await dialog.getByText("A foto deve ter até 1 MB.", { exact: true }).waitFor();

  const makeFixture = (color) => second.page.evaluate((fillStyle) => {
    const canvas = document.createElement("canvas");
    canvas.width = 24;
    canvas.height = 24;
    const context = canvas.getContext("2d");
    context.fillStyle = fillStyle;
    context.fillRect(0, 0, 24, 24);
    return canvas.toDataURL("image/png").split(",")[1];
  }, color);
  await picker.setInputFiles({ name: "avatar.png", mimeType: "image/png", buffer: Buffer.from(await makeFixture("#426f52"), "base64") });
  cleanups.push(async () => {
    const response = await second.page.request.delete(path, { headers: { Origin: webOrigin }, timeout: waitTimeout });
    assert.equal(response.status(), 204, "Limpeza da foto de teste");
  });
  await clickAndCheck(second.page, dialog.getByRole("button", { name: "Salvar foto", exact: true }), "PUT", path, 204);
  await dialog.getByText("Foto de perfil salva.", { exact: true }).waitFor();

  const newSession = await signIn(browser, "jogador2.dev", password);
  const freshAvatar = newSession.page.getByRole("banner").getByRole("button", { name: "Abrir conta", exact: true });
  const freshImageSize = await freshAvatar.locator("img").evaluate(async (image) => {
    await image.decode();
    return [image.naturalWidth, image.naturalHeight];
  });
  assert.deepEqual(freshImageSize, [192, 192]);
  const beforeRemoval = await first.page.request.get(path, { timeout: waitTimeout });
  const afterUpload = await second.page.request.get(path, { timeout: waitTimeout });
  assert.equal(afterUpload.status(), 200);
  assert.equal(afterUpload.headers()["content-type"], "image/png");
  assert.match(afterUpload.headers()["cache-control"], /private, no-store/);
  const originalBytes = await afterUpload.body();
  if (beforeRemoval.status() === 200) {
    assert.notDeepEqual(await beforeRemoval.body(), originalBytes, "A primeira conta não deve receber a foto da segunda.");
  } else {
    assert.equal(beforeRemoval.status(), 204);
  }

  await picker.setInputFiles({ name: "substituta.png", mimeType: "image/png", buffer: Buffer.from(await makeFixture("#b45d28"), "base64") });
  await clickAndCheck(second.page, dialog.getByRole("button", { name: "Salvar foto", exact: true }), "PUT", path, 204);
  await dialog.getByText("Foto de perfil salva.", { exact: true }).waitFor();
  const replaced = await second.page.request.get(path, { timeout: waitTimeout });
  assert.equal(replaced.status(), 200);
  assert.notDeepEqual(await replaced.body(), originalBytes);

  await clickAndCheck(second.page, dialog.getByRole("button", { name: "Remover foto", exact: true }), "DELETE", path, 204);
  await dialog.getByText("Foto de perfil removida.", { exact: true }).waitFor();
  await second.page.keyboard.press("Escape");
  await dialog.waitFor({ state: "hidden" });
  assert.equal((await second.page.request.get(path, { timeout: waitTimeout })).status(), 204);
}

test("uma vaga: entra na fila, promove o próximo e revoga acesso de quem sai", { timeout: 120_000 }, async (t) => {
  const password = process.env.APPLADA_E2E_TEST_PASSWORD;
  assert.ok(password, "Defina APPLADA_E2E_TEST_PASSWORD com a senha das três contas locais de teste.");

  const browserEnv = { ...process.env };
  delete browserEnv.APPLADA_E2E_TEST_PASSWORD;
  const browser = await chromium.launch({ channel: "chrome", headless: true, env: browserEnv });
  const cleanups = [];

  t.after(async () => {
    try {
      const results = await Promise.allSettled(cleanups.map((cleanup) => cleanup()));
      const failures = results.filter((result) => result.status === "rejected").map((result) => result.reason);
      if (failures.length > 0) {
        throw new AggregateError(failures, "Falha na limpeza dos dados do E2E.");
      }
    } finally {
      await browser.close();
    }
  });

  const organizer = await signIn(browser, "jogador3.dev", password);
  const first = await signIn(browser, "jogador1.dev", password);
  const second = await signIn(browser, "jogador2.dev", password);
  assert.equal(new Set([organizer.userId, first.userId, second.userId]).size, 3);

  const profilePath = "/api/local/profile";
  const profileBefore = await second.page.request.get(profilePath, { timeout: waitTimeout });
  assert.equal(profileBefore.status(), 200);
  const mismatchedAccount = await second.page.request.get(profilePath, {
    headers: { "X-Applada-Account": first.userId },
    timeout: waitTimeout,
  });
  assert.equal(mismatchedAccount.status(), 409, "O perfil não troca de conta dentro de uma aba já aberta.");
  const { displayName: previousName } = await profileBefore.json();
  if (previousName !== null) {
    cleanups.push(async () => {
      const response = await second.page.request.put(profilePath, {
        headers: { Origin: webOrigin },
        data: { displayName: previousName },
        timeout: waitTimeout,
      });
      assert.equal(response.status(), 200, "Restauração do nome de exibição do jogador de teste");
      assert.equal((await response.json()).displayName, previousName);
    });
  }

  const displayName = "Jogador da fila " + randomUUID().slice(0, 8);
  const profileCard = second.page.getByRole("region", { name: "Meu perfil", exact: true });
  const nameInput = profileCard.getByRole("textbox", { name: "Nome de exibição", exact: true });
  const account = second.page.getByRole("banner").getByRole("button", { name: "Abrir conta", exact: true });
  const dialog = second.page.getByRole("dialog", { name: "Sua conta" });
  const desktop = second.page.viewportSize();
  await second.page.setViewportSize({ width: 320, height: 700 });
  await account.click();
  await dialog.waitFor({ state: "visible" });
  const bounds = await dialog.boundingBox();
  assert.ok(bounds && bounds.x >= 0 && bounds.x + bounds.width <= 320, "O modal deve caber na tela compacta.");
  await dialog.getByRole("button", { name: "Configurações", exact: true }).click();
  await dialog.getByRole("link", { name: "Configurações do grupo", exact: true }).waitFor();
  await dialog.getByRole("button", { name: "Perfil", exact: true }).click();
  await dialog.getByRole("textbox", { name: "Nome de exibição", exact: true }).fill(displayName);
  const updatedProfile = await clickAndCheck(
    second.page, dialog.getByRole("button", { name: "Salvar nome", exact: true }), "PUT", profilePath, 200,
  );
  assert.equal((await updatedProfile.json()).displayName, displayName);
  await dialog.getByText("Nome de exibição salvo.", { exact: true }).waitFor({ state: "visible" });
  await second.page.keyboard.press("Escape");
  await dialog.waitFor({ state: "hidden" });
  assert.ok(await account.evaluate((button) => button === document.activeElement), "O foco volta ao avatar.");
  await second.page.setViewportSize(desktop);
  await second.page.reload();
  await nameInput.and(second.page.locator("input:enabled")).waitFor({ state: "visible" });
  assert.equal(await nameInput.inputValue(), displayName);
  if (process.env.APPLADA_E2E_AVATAR_STORAGE === "1") {
    await checkAvatar(browser, first, second, password, cleanups);
    t.diagnostic("Foto validada no Supabase Storage com limite, persistência e remoção.");
  }

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
  const matchId = match.id;
  cleanups.push(async () => {
    const response = await organizer.page.request.post(`/api/local/matches/${matchId}/cancellation`, {
      headers: { Origin: webOrigin },
      timeout: waitTimeout,
    });
    assert.equal(response.status(), 200, `Cancelamento da partida de teste ${matchId}`);
    assert.equal((await response.json()).status, "cancelled");
  });
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
  const participants = (await list.json()).items;
  assert.deepEqual(participants.map(({ userId, status }) => [userId, status]), [
    [first.userId, "confirmed"],
    [second.userId, "waitlisted"],
  ]);
  assert.equal(participants[1].displayName, displayName);
  await visibleList.waitFor({ state: "visible" });
  await firstCard.getByRole("listitem").filter({ hasText: displayName }).waitFor({ state: "visible" });

  await clickAndCheck(
    first.page, firstCard.getByRole("button", { name: "Retirar inscrição", exact: true }), "DELETE", registrations + "/me", 204,
  );
  await firstListButton.waitFor({ state: "hidden" });
  await visibleList.waitFor({ state: "hidden" });

  const denied = await first.page.request.get(registrations, { timeout: waitTimeout });
  assert.equal(denied.status(), 403, "O jogador que saiu não pode consultar os participantes");
  const problem = await denied.json();
  assert.equal(problem.type, "urn:applada:problem:participants-forbidden");
  assert.equal(problem.items, undefined);

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
  assert.deepEqual((await finalList.json()).items.map(({ userId, status, displayName }) => [userId, status, displayName]), [
    [second.userId, "confirmed", displayName],
  ]);
});
