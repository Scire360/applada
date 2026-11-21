import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import test from "node:test";
import { launchChrome, requirePassword, runCleanups, signIn, waitTimeout, webOrigin } from "./sessao.mjs";

test("organizador cadastra uma quadra e jogador encontra a pelada e entra", { timeout: 180_000 }, async (t) => {
  const password = requirePassword();
  const browser = await launchChrome();
  const cleanups = [];
  t.after(() => runCleanups(cleanups, browser));
  const owner = await signIn(browser, "jogador3.dev", password, "/peladas/conectadas");
  const player = await signIn(browser, "jogador1.dev", password, "/peladas/abertas");
  assert.notEqual(owner.userId, player.userId);
  const position = { latitude: -20.469712, longitude: -54.620123 };
  const suffix = randomUUID().slice(0, 8);
  const venueName = `Quadra E2E ${suffix}`;
  const title = `Pelada E2E ${suffix}`;

  await owner.context.setGeolocation(position);
  await owner.context.grantPermissions(["geolocation"], { origin: webOrigin });
  const organize = owner.page.getByRole("region", { name: "Organizar partida" });
  await organize.getByRole("button", { name: "Cadastrar local", exact: true }).click();
  assert.equal(await organize.getByLabel("Nome do local").evaluate((el) => el === document.activeElement), true);
  await organize.getByRole("button", { name: "Cancelar cadastro" }).click();
  assert.equal(await organize.getByLabel("Meus locais").evaluate((el) => el === document.activeElement), true);
  await organize.getByRole("button", { name: "Cadastrar local", exact: true }).click();
  await organize.getByLabel("Nome do local").fill(venueName);
  await organize.getByLabel("Endereço").fill("Quadra pública de teste");
  await organize.getByRole("button", { name: "Usar minha localização atual" }).click();
  await owner.page.waitForFunction(() => document.querySelector('input[name="latitude"]')?.value === "-20.47");
  await organize.getByLabel("Longitude").fill("-54.621");
  const [venue] = await Promise.all([
    owner.page.waitForResponse((r) => r.url() === webOrigin + "/api/app/venues" && r.request().method() === "POST"),
    organize.getByRole("button", { name: "Salvar local" }).click(),
  ]);
  assert.equal(venue.status(), 201, "Cadastro do local pelo BFF");
  const { id: venueId } = await venue.json();
  await organize.getByRole("option", { name: venueName }).waitFor({ state: "attached" });
  assert.equal(await organize.getByLabel("Meus locais").inputValue(), venueId);
  assert.equal(await organize.getByLabel("Meus locais").evaluate((el) => el === document.activeElement), true);

  await organize.getByLabel("Nome da partida").fill(title);
  const start = await owner.page.evaluate(() => {
    const future = new Date(Date.now() + 24 * 60 * 60_000);
    return new Date(future.getTime() - future.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
  });
  await organize.getByLabel("Início no horário local do seu dispositivo").fill(start);
  const [created] = await Promise.all([
    owner.page.waitForResponse((r) => r.url() === webOrigin + "/api/app/matches" && r.request().method() === "POST"),
    organize.getByRole("button", { name: "Criar partida", exact: true }).click(),
  ]);
  assert.equal(created.status(), 201, "Criação da partida pelo BFF");
  const { id } = await created.json();
  cleanups.push(async () => {
    const result = await owner.page.request.post(`/api/app/matches/${id}/cancellation`, {
      headers: { Origin: webOrigin }, timeout: waitTimeout,
    });
    assert.equal(result.status(), 200, "Cancelamento da partida de teste");
  });
  assert.equal(created.request().postDataJSON().venueId, venueId);

  const locate = player.page.getByRole("button", { name: "Usar minha localização", exact: true });
  await player.context.grantPermissions([], { origin: webOrigin });
  await locate.click();
  await player.page.getByRole("alert").filter({ hasText: "Libere a localização" }).waitFor();

  await player.context.setGeolocation(position);
  await player.context.grantPermissions(["geolocation"], { origin: webOrigin });
  const [found] = await Promise.all([
    player.page.waitForResponse((r) => new URL(r.url()).pathname === "/api/app/discover/matches"),
    locate.click(),
  ]);
  assert.equal(found.status(), 200, "Busca pelo BFF");
  assert.equal(await found.request().headerValue("X-Applada-Lat"), "-20.47");
  assert.equal(await found.request().headerValue("X-Applada-Lon"), "-54.62");
  assert.equal(new URL(found.url()).searchParams.has("lat"), false);
  assert.equal(player.page.url(), webOrigin + "/peladas/abertas");

  const match = player.page.getByRole("listitem").filter({ has: player.page.getByRole("heading", { name: title, exact: true }) });
  const more = player.page.getByRole("button", { name: "Ver mais", exact: true });
  await player.page.getByRole("list", { name: "Partidas encontradas" }).waitFor();
  while (!(await match.count()) && await more.isVisible()) {
    await Promise.all([
      player.page.waitForResponse((r) => new URL(r.url()).pathname === "/api/app/discover/matches"),
      more.click(),
    ]);
    await player.page.getByRole("status").filter({ hasText: "Carregando partidas" }).waitFor({ state: "hidden" });
  }
  await match.getByText(venueName, { exact: true }).waitFor();
  await match.getByRole("button", { name: "Entrar", exact: true }).click();
  const registration = match.getByText("Confirmado", { exact: true });
  await registration.waitFor();
  assert.equal(await registration.evaluate((el) => el === document.activeElement), true);
  const search = player.page.getByRole("region", { name: "Partidas perto de mim" });
  await search.getByRole("button", { name: "Usar minha localização", disabled: false }).waitFor();
  const announcement = search.getByRole("status");
  assert.match(await announcement.innerText(), /Confirmado/);
  assert.ok((await announcement.innerText()).includes(title));
});
