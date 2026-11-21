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
