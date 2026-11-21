import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import test from "node:test";
import { launchChrome, requirePassword, signIn, webOrigin } from "./sessao.mjs";

// O login do Keycloak só redireciona de volta para a porta 3000.
const base = webOrigin;

const viewports = [
  [320, 568], [360, 800], [390, 844], [430, 932], [568, 320],
  [640, 820], [641, 820], [690, 820], [768, 1024], [844, 390],
  [900, 900], [901, 900], [1024, 768], [1120, 900], [1121, 900],
  [1280, 800], [1366, 768], [1440, 900], [1536, 864], [1707, 960],
  [1919, 1080], [1920, 1080], [2048, 1152], [2560, 1440], [3440, 1440], [3840, 2160],
].map(([width, height]) => ({ width, height }));

async function appRoutes() {
  const screens = await readFile(new URL("../../src/lib/screens.ts", import.meta.url), "utf8");
  const catalog = Array.from(screens.matchAll(/^\s*path:\s*"([^"]+)",/gm), (match) => match[1]);
  const pages = await readdir(new URL("../../src/app", import.meta.url), { recursive: true });
  const fixed = pages.map((path) => path.replaceAll("\\", "/"))
    .filter((path) => /(^|\/)page\.tsx$/.test(path) && !path.includes("["))
    .map((path) => "/" + path.replace(/\/?page\.tsx$/, ""));
  // Páginas sem o shell do app (login, convite público e acesso negado) não entram na medição.
  const withoutShell = new Set(["/login", "/convite", "/acesso-negado"]);
  return [...new Set([...fixed, ...catalog])].filter((route) => !withoutShell.has(route)).sort();
}

const adminOnlyRoute = /^\/(administracao|modulos)(\/|$)/;

async function assertAttendanceFeedbackAt320px(page, title) {
  const row = page.getByRole("listitem").filter({ hasText: title });
  const text = "Não foi possível verificar a chamada.";
  const message = row.getByText(text, { exact: true });
  await message.waitFor();
  const status = row.getByRole("status").filter({ hasText: text });
  assert.equal(await status.count(), 1, "A mensagem da chamada deve estar na região de status");
  assert.equal(await status.evaluate(element => element.matches(".form-feedback.form-feedback-error")), true,
    "O aviso deve usar o padrão de feedback de erro");
  const button = status.getByRole("button", { name: "Tentar de novo a consulta da chamada de " + title });
  assert.equal(await button.isVisible(), true);
  const textBox = await message.boundingBox();
  const buttonBox = await button.boundingBox();
  const rowBox = await row.boundingBox();
  assert.ok(buttonBox.y > textBox.y + textBox.height, "O botão deve quebrar para a linha seguinte com espaço");
  for (const box of [textBox, buttonBox]) {
    assert.ok(box.x >= Math.max(0, rowBox.x) && box.x + box.width <= Math.min(rowBox.x + rowBox.width, 320));
  }
}

async function assertAttendanceRetryFocus(page, match, userId) {
  const row = page.getByRole("listitem").filter({ hasText: match.title });
  const retry = row.getByRole("button", { name: "Tentar de novo a consulta da chamada de " + match.title });
  const focused = element => element === document.activeElement;
  await retry.focus();
  for (const success of [false, true]) {
    const reply = Promise.withResolvers();
    const path = "**/api/app/matches/" + match.id;
    await page.route(path, async route => {
      await reply.promise;
      await route.fulfill(success ? { json: { ...match, organizerId: userId } } : { status: 503, json: {} });
    }, { times: 1 });
    try {
      await Promise.all([page.waitForRequest(path), page.keyboard.press("Enter")]);
      const checking = row.getByRole("button", { name: "Verificando…" });
      assert.equal(await checking.count(), 1, "O botão deve continuar montado durante a consulta");
      assert.equal(await checking.isDisabled(), true);
      assert.equal(await checking.evaluate(focused), true);
    } finally {
      reply.resolve();
    }
    const target = success ? row.getByRole("link", { name: "Fazer chamada" }) : retry;
    await target.waitFor();
    assert.equal(await target.evaluate(focused), true);
  }
}

test("botões da descoberta mostram a operação em andamento", async (t) => {
  const browser = await launchChrome();
  t.after(() => browser.close());
  const page = await browser.newPage();
  await page.setContent(`
    <main class="connected-page">
      <button class="button button-primary" aria-disabled="true">Entrar</button>
      <button class="button button-primary" aria-disabled="true" type="submit">Salvar local</button>
      <div class="attendance-link-feedback"><button aria-disabled="true">Verificando…</button></div>
    </main>
  `);
  await page.addStyleTag({ content: await readFile(new URL("../../src/app/globals.css", import.meta.url), "utf8") });
  const styles = await page.getByRole("button").evaluateAll(buttons => buttons.map(button => {
    const { opacity, cursor } = getComputedStyle(button);
    return { label: button.textContent, opacity, cursor };
  }));
  assert.deepEqual(styles, [
    { label: "Entrar", opacity: "0.55", cursor: "wait" },
    { label: "Salvar local", opacity: "0.55", cursor: "wait" },
    { label: "Verificando…", opacity: "0.55", cursor: "progress" },
  ]);
  const join = page.getByRole("button", { name: "Entrar" });
  await join.evaluate(button => button.setAttribute("aria-disabled", "false"));
  assert.equal(await join.evaluate(button => getComputedStyle(button).opacity), "1");
  assert.notEqual(await join.evaluate(button => getComputedStyle(button).cursor), "wait");
});

test("aviso da chamada em 320 px agrupa os controles e preserva o foco ao tentar de novo", async () => {
  const browser = await launchChrome();
  try {
    const { page, userId } = await signIn(browser, "jogador1.dev", requirePassword());
    await page.setViewportSize({ width: 320, height: 568 });
    const match = {
      id: "f0cf5702-1234-4321-a987-27aa3a0a88ab", title: "Pelada de sábado",
      startsAt: new Date(Date.now() - 60_000).toISOString(), durationMinutes: 90,
      status: "scheduled", confirmedCount: 1, capacity: 10, myStatus: "confirmed",
    };
    await page.route("**/api/app/me/matches?role=participant", route =>
      route.fulfill({ json: { items: [match], nextCursor: null } }));
    await page.route("**/api/app/matches/" + match.id, route => route.fulfill({ status: 503, json: {} }));
    await page.goto("/peladas/conectadas");
    await page.getByRole("button", { name: "Participo", exact: true }).click();

    await assertAttendanceFeedbackAt320px(page, match.title);
    await assertAttendanceRetryFocus(page, match, userId);

    const groupId = "78a3fc46-ad31-4c34-90f0-1f13be632570";
    await page.route("**/api/app/groups/" + groupId, route =>
      route.fulfill({ json: { id: groupId, name: "Grupo de sábado", myRole: "member", memberCount: 1 } }));
    await page.route(`**/api/app/groups/${groupId}/members`, route =>
      route.fulfill({ json: { items: [], nextCursor: null } }));
    await page.route(`**/api/app/groups/${groupId}/matches`, route =>
      route.fulfill({ json: { items: [match], nextCursor: null } }));
    await page.goto("/grupos/" + groupId);
    await assertAttendanceFeedbackAt320px(page, match.title);
  } finally {
    await browser.close();
  }
});

async function measureLayout() {
  await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  const shell = Array.from(document.querySelectorAll(".app-shell"))
    .find((element) => element.getClientRects().length > 0);
  const main = shell.querySelector(".main-column");
  const content = shell.querySelector(".page-content");
  const breadcrumbs = shell.querySelector(".breadcrumbs");
  const account = shell.querySelector(".topbar .account-trigger");
  const community = shell.querySelector('.compact-primary-nav a[aria-label="Comunidade"]');
  const range = document.createRange();
  range.selectNodeContents(community.querySelector("span"));

  const contentRect = content.getBoundingClientRect();
  const contentStyle = getComputedStyle(content);
  const leftPadding = parseFloat(contentStyle.paddingLeft);
  const rightPadding = parseFloat(contentStyle.paddingRight);
  const accountRect = account.getBoundingClientRect();
  const layoutRect = content.firstElementChild.getBoundingClientRect();
  const communityRect = community.getBoundingClientRect();
  const communityText = Array.from(range.getClientRects());
  const hero = content.querySelector(".match-hero")?.getBoundingClientRect();
  const agenda = content.querySelector("#agenda")?.getBoundingClientRect();
  const viewportWidth = document.documentElement.clientWidth;
  return {
    overflow: Math.max(document.documentElement.scrollWidth, main.scrollWidth + main.offsetLeft) - viewportWidth,
    available: main.clientWidth,
    used: Math.min(layoutRect.width, contentRect.width - leftPadding - rightPadding),
    headerOffset: Math.abs(breadcrumbs.getBoundingClientRect().left - contentRect.left - leftPadding),
    accountFits: accountRect.left >= 0 && accountRect.right <= viewportWidth
      && accountRect.width >= 44 && accountRect.height >= 44,
    communityFits: communityText.length === 1 && communityText[0].left >= communityRect.left
      && communityText[0].right <= communityRect.right,
    actionsFit: Array.from(content.querySelectorAll(".page-heading-actions > *")).every((action) => {
      const rect = action.getBoundingClientRect();
      return rect.left >= layoutRect.left - 1 && rect.right <= layoutRect.right + 1;
    }),
    agendaBesideHero: hero && agenda && agenda.left >= hero.right && Math.abs(agenda.top - hero.top) <= 1,
  };
}

test("todas as páginas cabem nos tamanhos suportados e aproveitam telas largas", { timeout: 240_000 }, async (t) => {
  const routes = await appRoutes();
  const browser = await launchChrome();
  const { context } = await signIn(browser, "jogador1.dev", requirePassword());
  const failures = [];
  let checked = 0;
  try {
    await Promise.all(Array.from({ length: 3 }, async (_, worker) => {
      const page = await context.newPage();
      page.on("pageerror", (error) => {
        const { width, height } = page.viewportSize();
        failures.push(new URL(page.url()).pathname + " em " + width + "×" + height
          + " tem erro de execução: " + error.message);
      });
      try {
        for (const route of routes.filter((_, index) => index % 3 === worker)) {
          await page.setViewportSize(viewports[0]);
          await page.emulateMedia({ reducedMotion: "reduce" });
          const [, response] = await Promise.all([
            page.waitForResponse((response) => response.url() === base + "/api/app/me"),
            page.goto(base + route, { waitUntil: "load" }),
          ]);
          assert.equal(response?.status(), 200, route);
          await page.getByRole("main").waitFor({ state: "visible" });
          // jogador1.dev não é administrador: as áreas administrativas devem negar o acesso, sem shell para medir.
          if (new URL(page.url()).pathname === "/acesso-negado") {
            if (!adminOnlyRoute.test(route)) failures.push(route + " negou o acesso a uma conta sem papel de administrador");
            continue;
          }
          // O shell chega por streaming (Suspense) depois do load: antes dele só existe o aviso "Carregando".
          await page.locator(".app-shell:visible").waitFor();
          for (const viewport of viewports) {
            await page.setViewportSize(viewport);
            const info = await page.evaluate(measureLayout);
            const context = route + " em " + viewport.width + "×" + viewport.height;
            if (info.overflow > 1) failures.push(context + " excede " + info.overflow + " px");
            if (!info.accountFits) failures.push(context + " corta o botão da conta");
            if (!info.actionsFit) failures.push(context + " corta as ações do cabeçalho da página");
            if (viewport.width === 320 && !info.communityFits) failures.push(context + " quebra o rótulo Comunidade");
            if (route === "/peladas" && info.agendaBesideHero !== (viewport.width >= 1920)) {
              failures.push(context + " posiciona a agenda na coluna errada");
            }
            if (viewport.width >= 1920 && (info.used < info.available * 0.84 || info.headerOffset > 1)) {
              failures.push(context + " usa " + Math.round(info.used) + " de " + info.available
                + " px, com deslocamento de " + Math.round(info.headerOffset) + " px em relação ao cabeçalho");
            }
            checked++;
          }
        }
      } finally {
        await page.close();
      }
    }));
  } finally {
    await browser.close();
  }
  t.diagnostic(routes.length + " páginas × " + viewports.length + " telas, " + checked + " combinações");
  const examples = failures.filter((failure) => failure.includes(" excede ") || failure.includes(" corta "));
  assert.equal(failures.length, 0, [...new Set([...examples, ...failures])].slice(0, 22).join("\n"));
});

test("seções do sidebar começam fechadas e os atalhos compactos ficam acessíveis", async () => {
  const browser = await launchChrome();
  try {
    const { context } = await signIn(browser, "jogador1.dev", requirePassword());
    const page = await context.newPage();
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(base + "/peladas");
    const nav = page.getByRole("navigation", { name: "Navegação principal" });
    // O shell chega por streaming (Suspense) depois do load.
    await nav.waitFor();
    const groups = nav.locator("details");
    assert.deepEqual(await groups.locator("summary").allTextContents(), [
      "Menu", "Dentro de campo", "Comunidade", "Acompanhamento", "Ferramentas",
    ]);
    assert.equal(await nav.locator("details[open]").count(), 0);
    assert.equal(await nav.getByRole("link").count(), 0);

    const menu = groups.filter({ has: page.locator("summary").getByText("Menu", { exact: true }) });
    const field = groups.filter({ has: page.locator("summary").getByText("Dentro de campo", { exact: true }) });
    await menu.locator("summary").press("Enter");
    assert.equal(await nav.getByRole("link", { name: "Peladas", exact: true }).getAttribute("aria-current"), "page");
    await field.locator("summary").click();
    assert.equal(await nav.locator("details[open]").count(), 2);
    await menu.locator("summary").press("Space");
    assert.equal(await nav.getByRole("link", { name: "Peladas", exact: true }).count(), 0);
    assert.equal(await nav.getByRole("link", { name: "Organizar pelada", exact: true }).isVisible(), true);
    await page.reload();
    await nav.waitFor();
    assert.equal(await nav.locator("details[open]").count(), 0);

    for (const width of [768, 320]) {
      await page.setViewportSize({ width, height: 820 });
      assert.equal(await groups.locator("summary:visible").count(), 0);
      assert.deepEqual(await nav.getByRole("link").evaluateAll((links) => links.map((link) => link.getAttribute("href"))), [
        "/", "/peladas", "/grupos", "/jogadores", "/comunidade", "/financeiro",
      ]);
    }
  } finally {
    await browser.close();
  }
});
