import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import test from "node:test";
import {
  fillKeycloak, isKeycloak, launchChrome, newContext, requirePassword, runCleanups, signIn, waitTimeout, webOrigin,
} from "./sessao.mjs";

const guid = "[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}";

// Dá um nome de exibição único à conta e agenda a restauração do nome anterior, se havia um.
async function setDisplayName(player, displayName, cleanups) {
  const path = "/api/app/profile";
  const before = await player.page.request.get(path, { timeout: waitTimeout });
  assert.equal(before.status(), 200, "Leitura do perfil do jogador de teste");
  const { displayName: previousName } = await before.json();
  if (previousName !== null) {
    cleanups.push(async () => {
      const response = await player.page.request.put(path, {
        headers: { Origin: webOrigin }, data: { displayName: previousName }, timeout: waitTimeout,
      });
      assert.equal(response.status(), 200, "Restauração do nome de exibição do jogador de teste");
    });
  }
  const saved = await player.page.request.put(path, {
    headers: { Origin: webOrigin }, data: { displayName }, timeout: waitTimeout,
  });
  assert.equal(saved.status(), 200, "Gravação do nome de exibição do jogador de teste");
}

test("grupo: convite sem sessão e já logado, promoção, partida do grupo e remoção", { timeout: 180_000 }, async (t) => {
  const password = requirePassword();
  const browser = await launchChrome();
  const cleanups = [];
  t.after(() => runCleanups(cleanups, browser));

  const owner = await signIn(browser, "jogador3.dev", password, "/grupos");
  const first = await signIn(browser, "jogador1.dev", password, "/grupos");
  const second = await signIn(browser, "jogador2.dev", password, "/grupos");
  assert.equal(new Set([owner.userId, first.userId, second.userId]).size, 3, "As três contas devem ser distintas");

  const suffix = randomUUID().slice(0, 8);
  const firstName = `Admin ${suffix}`;
  const secondName = `Membro ${suffix}`;
  await setDisplayName(owner, `Dono ${suffix}`, cleanups);
  await setDisplayName(first, firstName, cleanups);
  await setDisplayName(second, secondName, cleanups);

  // 0. O BFF recusa escrita com Origin de outro site ou sem Origin.
  for (const headers of [{ Origin: "https://evil.example" }, {}]) {
    const refused = await owner.page.request.post("/api/app/groups", { headers, data: { name: "Origem recusada" }, timeout: waitTimeout });
    assert.equal(refused.status(), 403, `POST /api/app/groups com Origin ${headers.Origin ?? "ausente"}`);
  }

  // 1. O dono cria o grupo.
  const groupName = `E2E grupo ${suffix}`;
  await owner.page.goto("/grupos");
  await owner.page.getByRole("heading", { name: "Meus grupos", exact: true }).waitFor();
  // Por papel: durante o streaming do Suspense o React mantém uma cópia oculta (#S:0) do formulário, que getByLabel também encontra.
  await owner.page.getByRole("textbox", { name: "Nome do grupo", exact: true }).fill(groupName);
  const groupUrl = new RegExp(`/grupos/(${guid})$`, "i");
  await Promise.all([
    owner.page.waitForURL(groupUrl),
    owner.page.getByRole("button", { name: "Criar grupo", exact: true }).click(),
  ]);
  const groupPath = new URL(owner.page.url()).pathname;
  const groupId = groupPath.match(groupUrl)[1];
  await owner.page.getByRole("heading", { level: 1, name: groupName, exact: true }).waitFor();

  // 2. O dono cria o convite e lê o link (mostrado só uma vez).
  await owner.page.getByRole("button", { name: "Criar convite", exact: true }).click();
  await owner.page.getByText("Convite criado. Copie o link agora: ele não será mostrado de novo.", { exact: true }).waitFor();
  const inviteLink = await owner.page.getByLabel("Link do convite", { exact: true }).inputValue();
  assert.match(inviteLink, new RegExp(`^${webOrigin}/convite#.+`), "Formato do link do convite");

  // 3. jogador1 abre o link sem sessão, entra pelo Keycloak e aceita ao voltar.
  const guestContext = await newContext(browser);
  const guest = await guestContext.newPage();
  await guest.goto(inviteLink);
  const signInButton = guest.getByRole("button", { name: "Entrar na minha conta", exact: true });
  await signInButton.waitFor();
  assert.equal(new URL(guest.url()).hash, "", "O token deve sair da barra de endereço.");
  await Promise.all([
    guest.waitForURL((url) => isKeycloak(url)),
    signInButton.click(),
  ]);
  await fillKeycloak(guest, "jogador1.dev", password);
  await guest.waitForURL((url) => url.origin === webOrigin && url.pathname === "/convite" && url.searchParams.get("retomar") === "1");
  await Promise.all([
    guest.waitForURL(groupUrl),
    guest.getByRole("button", { name: "Entrar no grupo", exact: true }).click(),
  ]);
  assert.equal(new URL(guest.url()).pathname, groupPath, "jogador1 entra no grupo do convite");
  await guest.getByRole("heading", { level: 1, name: groupName, exact: true }).waitFor();

  // 4. jogador2 já está logado e aceita o mesmo convite.
  await second.page.goto(inviteLink);
  await Promise.all([
    second.page.waitForURL(groupUrl),
    second.page.getByRole("button", { name: "Entrar no grupo", exact: true }).click(),
  ]);
  assert.equal(new URL(second.page.url()).pathname, groupPath, "jogador2 entra no grupo do convite");
  await second.page.getByRole("heading", { level: 1, name: groupName, exact: true }).waitFor();

  // 5. O dono promove jogador1 a administrador.
  await owner.page.reload();
  const members = owner.page.getByRole("region", { name: "Membros", exact: true });
  const promote = members.getByRole("button", { name: `Promover ${firstName}`, exact: true });
  await promote.click();
  await members.getByText("Membro promovido a administrador.", { exact: true }).waitFor();
  await members.getByRole("button", { name: `Rebaixar ${firstName}`, exact: true }).waitFor();

  // 6. jogador1 (agora admin) cria uma partida do grupo.
  await guest.reload();
  await Promise.all([
    guest.waitForURL((url) => url.pathname === "/peladas/conectadas" && url.searchParams.get("grupo") === groupId),
    guest.getByRole("link", { name: "Criar partida para o grupo", exact: true }).click(),
  ]);
  const title = `E2E partida do grupo ${randomUUID()}`;
  const form = guest.getByRole("region", { name: "Organizar partida", exact: true });
  const startsAt = await guest.evaluate(() => {
    const future = new Date(Date.now() + 24 * 60 * 60_000);
    return new Date(future.getTime() - future.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
  });
  await form.getByLabel("Nome da partida").fill(title);
  await form.getByLabel("Número de vagas").fill("5");
  await form.getByLabel("Início no horário local do seu dispositivo").fill(startsAt);
  await form.locator('select[name="grupo"]').selectOption(groupId);
  const [created] = await Promise.all([
    guest.waitForResponse((r) => r.url() === `${webOrigin}/api/app/matches` && r.request().method() === "POST"),
    form.getByRole("button", { name: "Criar partida", exact: true }).click(),
  ]);
  assert.equal(created.status(), 201, "POST /api/app/matches");
  const matchId = (await created.json()).id;
  cleanups.push(async () => {
    const response = await guest.request.post(`/api/app/matches/${matchId}/cancellation`, {
      headers: { Origin: webOrigin }, timeout: waitTimeout,
    });
    assert.equal(response.status(), 200, `Cancelamento da partida de teste ${matchId}`);
  });

  // 7. jogador2 vê a partida em "Próximas partidas".
  await second.page.goto(groupPath);
  const upcoming = second.page.getByRole("region", { name: "Próximas partidas", exact: true });
  await upcoming.getByRole("link", { name: new RegExp(title) }).waitFor();

  // 8. O dono remove jogador2, que deixa de enxergar o grupo.
  await owner.page.reload();
  await members.getByRole("button", { name: `Remover ${secondName}`, exact: true }).click();
  const confirmRemoval = members.getByRole("button", { name: `Confirmar remoção de ${secondName}`, exact: true });
  await confirmRemoval.waitFor();
  assert.equal(await confirmRemoval.evaluate((element) => element === document.activeElement), true, "O foco vai para o botão de confirmação.");
  await members.getByRole("button", { name: "Cancelar", exact: true }).click();
  await members.getByRole("button", { name: `Remover ${secondName}`, exact: true }).click();
  await confirmRemoval.click();
  await members.getByText("Membro removido.", { exact: true }).waitFor();
  await second.page.reload();
  await second.page.getByRole("heading", { name: "Grupo não encontrado.", exact: true }).waitFor();
});
