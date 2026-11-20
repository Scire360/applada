import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import test from "node:test";
import { chromium } from "playwright";

const base = "http://localhost:" + (process.env.APPLADA_LAYOUT_TEST_PORT ?? "3000");

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
  return [...new Set([...fixed, ...catalog])]
    .filter((route) => route !== "/login" && route !== "/acesso-negado")
    .sort();
}

async function measureLayout() {
  await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  const shell = Array.from(document.querySelectorAll(".app-shell"))
    .find((element) => element.getClientRects().length > 0);
  const main = shell.querySelector(".main-column");
  const content = shell.querySelector(".page-content");
  const breadcrumbs = shell.querySelector(".breadcrumbs");
  const account = shell.querySelector(".topbar .account-trigger");
  const avatar = shell.querySelector(".topbar .avatar-light");
  const identity = account ?? avatar;
  const community = shell.querySelector('.compact-primary-nav a[aria-label="Comunidade"]');
  const range = document.createRange();
  range.selectNodeContents(community.querySelector("span"));

  const contentRect = content.getBoundingClientRect();
  const contentStyle = getComputedStyle(content);
  const leftPadding = parseFloat(contentStyle.paddingLeft);
  const rightPadding = parseFloat(contentStyle.paddingRight);
  const identityRect = identity?.getClientRects().length ? identity.getBoundingClientRect() : null;
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
    accountFits: !identityRect || (identityRect.left >= 0 && identityRect.right <= viewportWidth
      && (!account || (identityRect.width >= 44 && identityRect.height >= 44))),
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
  const browser = await chromium.launch({ channel: "chrome", headless: true });
  const failures = [];
  let checked = 0;
  try {
    await Promise.all(Array.from({ length: 3 }, async (_, worker) => {
      const page = await browser.newPage({ viewport: viewports[0], reducedMotion: "reduce" });
      page.on("pageerror", (error) => {
        const { width, height } = page.viewportSize();
        failures.push(new URL(page.url()).pathname + " em " + width + "×" + height
          + " tem erro de execução: " + error.message);
      });
      try {
        for (const route of routes.filter((_, index) => index % 3 === worker)) {
          await page.setViewportSize(viewports[0]);
          const response = await page.goto(base + route, { waitUntil: "load" });
          await page.getByRole("main").waitFor({ state: "visible" });
          assert.equal(response?.status(), 200, route);
          assert.equal(new URL(page.url()).pathname, route, route);
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
  const browser = await chromium.launch({ channel: "chrome", headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    await page.goto(base + "/peladas");
    const nav = page.getByRole("navigation", { name: "Navegação principal" });
    const groups = nav.locator("details");
    assert.deepEqual(await groups.locator("summary").allTextContents(), [
      "Menu", "Dentro de campo", "Comunidade", "Acompanhamento", "Espaço do grupo", "Ferramentas",
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
    assert.equal(await nav.locator("details[open]").count(), 0);

    for (const width of [768, 320]) {
      await page.setViewportSize({ width, height: 820 });
      assert.equal(await groups.locator("summary:visible").count(), 0);
      assert.deepEqual(await nav.getByRole("link").evaluateAll((links) => links.map((link) => link.getAttribute("href"))), [
        "/", "/peladas", "/jogadores", "/comunidade", "/financeiro",
      ]);
    }
  } finally {
    await browser.close();
  }
});

test("prévia visual mantém o BFF protegido", async () => {
  const response = await fetch(`${base}/api/backend/session`, { redirect: "manual" });

  assert.equal(response.status, 401);
  assert.match(response.headers.get("content-type") ?? "", /^application\/problem\+json(?:;|$)/i);
});

test("indicadores do menu ficam à direita e legíveis no hover", async () => {
  const stylesheet = await readFile(new URL("../../src/app/globals.css", import.meta.url), "utf8");
  const browser = await chromium.launch({ channel: "chrome", headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
    await page.setContent(`
      <div class="app-shell">
        <aside class="sidebar">
          <nav class="primary-nav">
            <details class="nav-section">
              <summary class="nav-heading">
                <span>Dentro de campo</span>
                <svg aria-hidden="true" viewBox="0 0 24 24"><path d="m7 10 5 5 5-5" /></svg>
              </summary>
            </details>
          </nav>
        </aside>
      </div>
    `);
    await page.addStyleTag({ content: stylesheet });
    const heading = page.locator(".nav-heading");
    const alignment = await heading.evaluate((element) => {
      const bounds = element.getBoundingClientRect();
      const arrow = element.querySelector("svg").getBoundingClientRect();
      return {
        arrowCenter: arrow.left + arrow.width / 2,
        headingLeft: bounds.left,
        headingWidth: bounds.width,
      };
    });
    assert.ok(alignment.arrowCenter > alignment.headingLeft + alignment.headingWidth * 0.75);

    await heading.hover();
    const hoverStyles = await heading.evaluate((element) => ({
      text: getComputedStyle(element).color,
      arrow: getComputedStyle(element.querySelector("svg")).color,
      indicator: getComputedStyle(element, "::after").color,
    }));
    assert.deepEqual(hoverStyles, {
      text: "rgb(200, 243, 29)",
      arrow: "rgb(200, 243, 29)",
      indicator: "rgb(200, 243, 29)",
    });
  } finally {
    await browser.close();
  }
});

test("placar usa tipografia esportiva e contraste de estádio", async () => {
  const stylesheet = await readFile(new URL("../../src/app/globals.css", import.meta.url), "utf8");
  const browser = await chromium.launch({ channel: "chrome", headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 320, height: 568 } });
    await page.setContent(`
      <main class="dashboard-page">
        <section class="page-heading"><h1>O futebol acontece</h1></section>
        <section class="game-scoreboard">
          <div class="game-score-team"><span>TIME VERDE</span><strong>2</strong></div>
          <span class="game-score-divider">×</span>
          <div class="game-score-team"><span>TIME AZUL</span><strong>1</strong></div>
        </section>
      </main>
    `);
    await page.addStyleTag({ content: stylesheet });
    const visualStyles = await page.evaluate(() => {
      const board = document.querySelector(".game-scoreboard");
      const score = board.querySelector(".game-score-team strong");
      const heading = document.querySelector(".page-heading h1");
      return {
        boardColor: getComputedStyle(board).backgroundColor,
        boardTopBorder: getComputedStyle(board).borderTopColor,
        scoreColor: getComputedStyle(score).color,
        scoreFont: getComputedStyle(score).fontFamily,
        headingTransform: getComputedStyle(heading).textTransform,
        fitsViewport: board.scrollWidth <= board.clientWidth,
      };
    });
    assert.equal(visualStyles.boardColor, "rgb(16, 42, 32)");
    assert.equal(visualStyles.boardTopBorder, "rgb(200, 243, 29)");
    assert.equal(visualStyles.scoreColor, "rgb(200, 243, 29)");
    assert.match(visualStyles.scoreFont, /Oswald/);
    assert.equal(visualStyles.headingTransform, "uppercase");
    assert.equal(visualStyles.fitsViewport, true);
  } finally {
    await browser.close();
  }
});

test("tela de login compartilha a tipografia esportiva sem overflow", async () => {
  const stylesheet = await readFile(new URL("../../src/app/globals.css", import.meta.url), "utf8");
  const browser = await chromium.launch({ channel: "chrome", headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 320, height: 568 } });
    await page.setContent(`
      <main class="login-page">
        <div class="login-card">
          <section class="login-story">
            <div class="login-story-copy">
              <h1>O jogo fica melhor<br /><em>quando é junto.</em></h1>
            </div>
          </section>
          <section class="login-panel"><h2>Entre no seu espaço.</h2></section>
        </div>
      </main>
    `);
    await page.addStyleTag({ content: stylesheet });
    const styles = await page.evaluate(() => ({
      storyFont: getComputedStyle(document.querySelector(".login-story h1")).fontFamily,
      storyTransform: getComputedStyle(document.querySelector(".login-story h1")).textTransform,
      emphasisStyle: getComputedStyle(document.querySelector(".login-story h1 em")).fontStyle,
      panelFont: getComputedStyle(document.querySelector(".login-panel h2")).fontFamily,
      topBorder: getComputedStyle(document.querySelector(".login-card")).borderTopColor,
      fitsViewport: document.documentElement.scrollWidth <= document.documentElement.clientWidth,
    }));
    assert.match(styles.storyFont, /Oswald/);
    assert.equal(styles.storyTransform, "uppercase");
    assert.equal(styles.emphasisStyle, "normal");
    assert.match(styles.panelFont, /Oswald/);
    assert.equal(styles.topBorder, "rgb(200, 243, 29)");
    assert.equal(styles.fitsViewport, true);
  } finally {
    await browser.close();
  }
});
