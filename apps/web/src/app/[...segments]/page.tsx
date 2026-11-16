import { Suspense } from "react";
import Link from "next/link";
import { notFound } from "next/navigation";
import { AppShell } from "@/components/app-shell";
import { GameDemoPanel } from "@/components/game-demo";
import { MatchCreationForm, MatchDemoMetrics, MatchDemoPanel } from "@/components/match-demo";
import { PreviewForm, type FormField } from "@/components/preview-form";
import { ScreenCatalog, type ScreenCatalogItem } from "@/components/screen-catalog";
import { ScreenDirectory, type DirectoryRecord } from "@/components/screen-directory";
import { Icon } from "@/components/icon";
import { routeByLegacyView, screenByLegacyView, screenByPath, screens } from "@/lib/screens";

const forms: Record<string, { submitLabel: string; fields: FormField[] }> = {
  "events-new": {
    submitLabel: "Pré-visualizar pelada",
    fields: [
      { name: "match-title", label: "Nome do encontro", placeholder: "Ex.: Quinta de futebol", required: true, wide: true },
      { name: "match-date", label: "Data", type: "date", required: true },
      { name: "match-time", label: "Horário", type: "time", required: true },
      { name: "match-location", label: "Local e campo", placeholder: "Arena, endereço ou ponto de encontro", required: true, wide: true },
      { name: "match-capacity", label: "Número de vagas", type: "number", placeholder: "18", required: true },
      { name: "match-notes", label: "Detalhes para o time", type: "textarea", placeholder: "O que os jogadores precisam saber?", wide: true },
    ],
  },
  "players-new": {
    submitLabel: "Pré-visualizar convite",
    fields: [
      { name: "player-name", label: "Nome completo", placeholder: "Nome do jogador", required: true },
      { name: "player-email", label: "E-mail", type: "email", placeholder: "jogador@email.com", required: true },
      { name: "player-position", label: "Posição", type: "select", options: ["Goleiro", "Defensor", "Meio-campo", "Atacante"], required: true },
      { name: "player-message", label: "Mensagem do convite", type: "textarea", placeholder: "Uma mensagem para dar as boas-vindas", wide: true },
    ],
  },
  "users-new": {
    submitLabel: "Pré-visualizar convite",
    fields: [
      { name: "user-name", label: "Nome", placeholder: "Nome da pessoa", required: true },
      { name: "user-email", label: "E-mail", type: "email", placeholder: "pessoa@email.com", required: true },
      { name: "user-role", label: "Acesso", type: "select", options: ["Jogador", "Organizador"], required: true },
    ],
  },
  "finance-user-new": {
    submitLabel: "Pré-visualizar acesso",
    fields: [
      { name: "finance-user-name", label: "Nome", placeholder: "Nome da pessoa", required: true },
      { name: "finance-user-email", label: "E-mail", type: "email", placeholder: "pessoa@email.com", required: true },
      { name: "finance-user-role", label: "Escopo de acesso", type: "select", options: ["Consultar financeiro", "Registrar movimentações"], required: true },
    ],
  },
  "finance-category-new": {
    submitLabel: "Pré-visualizar categoria",
    fields: [
      { name: "category-name", label: "Nome da categoria", placeholder: "Ex.: Aluguel da quadra", required: true },
      { name: "category-type", label: "Tipo", type: "select", options: ["Entrada", "Despesa"], required: true },
      { name: "category-description", label: "Descrição", type: "textarea", placeholder: "Para que esta categoria será utilizada?", wide: true },
    ],
  },
};

const recordsByGroup: Record<string, DirectoryRecord[]> = {
  matches: [
    { initials: "QUI", name: "Quinta de futebol", detail: "19h30 · Arena Santa Cecília · 14 de 18 confirmados", state: "Inscrições abertas", color: "mint" },
    { initials: "DOM", name: "Desafio de domingo", detail: "09h · Parque da Água Branca · 8 de 16 confirmados", state: "Inscrições abertas", color: "blue" },
    { initials: "QUI", name: "Quinta de futebol", detail: "19h30 · Arena Santa Cecília · próxima semana", state: "Em organização", color: "yellow" },
  ],
  players: [
    { initials: "LC", name: "Lucas Costa", detail: "Meio-campo · 96% de presença", state: "No grupo", color: "mint" },
    { initials: "BA", name: "Bruno Alves", detail: "Atacante · 92% de presença", state: "No grupo", color: "lavender" },
    { initials: "JP", name: "João Pedro", detail: "Goleiro · 88% de presença", state: "No grupo", color: "peach" },
  ],
  community: [
    { initials: "LC", name: "Lucas Costa", detail: "Confirmou presença na próxima pelada", state: "Há 12 min", color: "mint" },
    { initials: "BA", name: "Bruno Alves", detail: "Compartilhou um momento com o grupo", state: "Há 1 h", color: "lavender" },
    { initials: "RM", name: "Rafael Martins", detail: "Publicou uma lembrança da partida", state: "Ontem", color: "peach" },
  ],
  finance: [
    { initials: "LC", name: "Lucas Costa", detail: "Contribuição do grupo · novembro", state: "+ R$ 45,00", color: "mint" },
    { initials: "BA", name: "Bruno Alves", detail: "Contribuição do grupo · novembro", state: "+ R$ 45,00", color: "lavender" },
    { initials: "JP", name: "João Pedro", detail: "Rateio da quadra · novembro", state: "− R$ 180,00", color: "peach" },
  ],
  analytics: [
    { initials: "18", name: "Partidas realizadas", detail: "Encontros registrados nesta temporada", state: "Temporada 2026", color: "mint" },
    { initials: "87%", name: "Presença média", detail: "Participação do elenco nos jogos", state: "Em evolução", color: "blue" },
    { initials: "24", name: "Pessoas no elenco", detail: "Jogadores que fazem parte do grupo", state: "Grupo ativo", color: "yellow" },
  ],
  admin: [
    { initials: "24", name: "Pessoas e acessos", detail: "Integrantes e responsabilidades no espaço", state: "Ver lista", color: "mint" },
    { initials: "6", name: "Preferências do grupo", detail: "Identidade, canais e automações", state: "Configurar", color: "blue" },
    { initials: "1", name: "Espaço ativo", detail: "Santa Cecília FC · São Paulo, SP", state: "Em uso", color: "yellow" },
  ],
};

const legacyLabels: Record<string, string> = {
  "events-list": "Agenda de partidas",
  "checkin-center": "Check-in da partida",
  "match-center": "Central da partida",
  "presence-center": "Presença e confirmações",
  "players-list": "Elenco de jogadores",
  "players-new": "Adicionar jogador",
  "player-profile": "Perfil do jogador",
  "social-center": "Comunidade do grupo",
  "gallery-center": "Galeria do grupo",
  "finance-dashboard": "Visão financeira",
  "finance-entries": "Lançamentos financeiros",
  "finance-collections": "Contribuições do grupo",
  "finance-waivers": "Isenções do grupo",
  "finance-suspensions": "Pendências e suspensões",
  "finance-category-list": "Categorias financeiras",
  "finance-category-new": "Nova categoria",
  "finance-audit": "Histórico e transparência",
  "performance-center": "Desempenho do time",
  "reports-center": "Relatórios do grupo",
  "users-list": "Pessoas e acessos",
  "users-new": "Convidar uma pessoa",
  "admin-center": "Administração do grupo",
  "settings-center": "Configurações do grupo",
  "communication-center": "Canais de comunicação",
  "backup-restore": "Backup e recuperação",
  "import-center": "Importar dados",
  "birthdays-center": "Aniversários do time",
  "highlight-vote": "Destaques da pelada",
  "ai-score": "Leitura do placar",
  "open-matches": "Peladas abertas",
  "offers-center": "Benefícios do grupo",
  "notifications-center": "Central de notificações",
  "universal-search": "Buscar no Applada",
};

const staticRouteLabels: Record<string, string> = {
  "/": "Visão geral",
  "/peladas": "Agenda de partidas",
  "/jogadores": "Elenco de jogadores",
  "/comunidade": "Comunidade do grupo",
  "/financeiro": "Visão financeira",
};

const groupByPath: Record<string, ScreenCatalogItem["group"]> = {
  "/": "matches",
  "/peladas": "matches",
  "/jogadores": "players",
  "/comunidade": "community",
  "/financeiro": "finance",
};

function getLegacyLabel(legacyView: string) {
  return legacyLabels[legacyView] ?? legacyView.replaceAll("-", " ");
}

function getScreenCatalogItems(): ScreenCatalogItem[] {
  return Object.entries(routeByLegacyView).map(([legacyView, href]) => {
    const screen = screenByLegacyView.get(legacyView);
    return {
      href,
      label: screen?.title ?? staticRouteLabels[href] ?? getLegacyLabel(legacyView),
      group: screen?.group ?? groupByPath[href] ?? "admin",
    };
  });
}

const screenCatalogItems = getScreenCatalogItems();

export function generateStaticParams() {
  return screens.map(({ path }) => ({
    segments: path.split("/").filter(Boolean),
  }));
}

type LegacyScreenPageProps = {
  params: Promise<{ segments: string[] }>;
};

function LegacyScreenFallback() {
  return (
    <div aria-live="polite" className="screen-loading" role="status">
      Abrindo esta área do Applada…
    </div>
  );
}

export default function LegacyScreenPage({ params }: LegacyScreenPageProps) {
  return (
    <Suspense fallback={<LegacyScreenFallback />}>
      <LegacyScreenContent params={params} />
    </Suspense>
  );
}

async function LegacyScreenContent({
  params,
}: LegacyScreenPageProps) {
  const { segments } = await params;
  const path = `/${segments.join("/")}`;
  const screen = screenByPath.get(path);

  if (!screen) notFound();

  const form = forms[screen.legacyView];
  const nextScreens = screen.nextSteps
    .map((legacyView) => {
      const href = routeByLegacyView[legacyView];
      return href ? { href, label: getLegacyLabel(legacyView) } : null;
    })
    .filter((item): item is { href: string; label: string } => item !== null);
  const relatedLinks = screen.related.map((href) => {
    const relatedScreen = screenByPath.get(href);
    return {
      href,
      label: relatedScreen?.title ?? staticRouteLabels[href] ?? "Visão geral do grupo",
    };
  });
  const records = recordsByGroup[screen.group];
  const isModuleCatalog = screen.legacyView === "modules";
  const isMatchCreation = screen.legacyView === "events-new";
  const matchDemoMode = screen.legacyView === "open-matches"
    ? "open"
    : screen.legacyView === "presence-center" ? "presence" : "checkin";
  const hasMatchDemo = ["open-matches", "presence-center", "checkin-center"].includes(screen.legacyView);
  const isGameDemo = screen.legacyView === "match-center" || screen.legacyView === "highlight-vote";
  const gameDemoMode = screen.legacyView === "highlight-vote" ? "highlights" : "live";
  const isMatchFlow = isMatchCreation || hasMatchDemo || isGameDemo;
  const matchMetricsMode = isGameDemo
    ? "game"
    : screen.legacyView === "checkin-center" ? "checkin" : "attendance";
  const primaryTitle = isMatchCreation
    ? "Detalhes da pelada"
    : hasMatchDemo
      ? "Organização da partida"
      : isGameDemo
        ? gameDemoMode === "live" ? "Placar e lances" : "Destaque da partida"
        : form ? "Informações do grupo" : isModuleCatalog ? "Todas as áreas" : "Movimento recente";
  const primaryLabel = isMatchCreation
    ? "Crie e continue a demonstração"
    : isMatchFlow ? "Etapa demonstrativa"
      : form ? "Preencha para visualizar" : isModuleCatalog ? "Vistas do produto" : "Conteúdo ilustrativo";

  return (
    <AppShell active={screen.group} activePath={screen.path} title={screen.title}>
      <div className="module-page legacy-module-page">
        <section className="page-heading">
          <div>
            <p className="eyebrow page-eyebrow">{screen.eyebrow}</p>
            <h1>{screen.title}</h1>
            <p className="page-subtitle">{screen.description}</p>
          </div>
          {form ? (
            <a className="button button-primary" href="#formulario">
              <Icon name="plus" />
              {form.submitLabel}
            </a>
          ) : null}
        </section>

        {isMatchCreation ? null : isMatchFlow ? (
          <MatchDemoMetrics mode={matchMetricsMode} />
        ) : (
          <section aria-label={`Resumo: ${screen.title}`} className="screen-metric-grid">
            <article className="screen-metric-card">
              <span className="stat-icon stat-icon-green"><Icon name="sparkles" /></span>
              <span className="eyebrow">{screen.metrics[0]}</span>
              <strong>{screen.metrics[1]}</strong>
            </article>
            <article className="screen-metric-card">
              <span className="stat-icon stat-icon-blue"><Icon name="calendar" /></span>
              <span className="eyebrow">CONTEXTO</span>
              <strong>{screen.metrics[2]}</strong>
            </article>
            <article className="screen-metric-card screen-metric-note">
              <span className="eyebrow">SOBRE ESTA TELA</span>
              <strong>Dados de exemplo, sem ligação com contas reais.</strong>
              <span>Esta prévia não grava nem envia informações.</span>
            </article>
          </section>
        )}

        <section className="screen-workspace">
          <article className={`surface-card screen-primary-card${isMatchCreation ? " match-creation-card" : ""}`} id={form ? "formulario" : undefined}>
            <div className="section-title-row">
              <div>
                <p className="eyebrow">{isMatchCreation ? "DETALHES DO ENCONTRO" : isMatchFlow ? "DEMONSTRAÇÃO DA JORNADA" : form ? "ETAPA DEMONSTRATIVA" : "VISÃO DO ESPAÇO"}</p>
                <h2>{primaryTitle}</h2>
              </div>
              {isMatchCreation ? null : <span className="quiet-label">{primaryLabel}</span>}
            </div>
            {isMatchCreation ? (
              <MatchCreationForm />
            ) : form ? (
              <PreviewForm fields={form.fields} submitLabel={form.submitLabel} />
            ) : isModuleCatalog ? (
              <ScreenCatalog items={screenCatalogItems} />
            ) : isGameDemo ? (
              <GameDemoPanel mode={gameDemoMode} />
            ) : hasMatchDemo ? (
              <MatchDemoPanel mode={matchDemoMode} />
            ) : (
              <ScreenDirectory records={records} related={relatedLinks} />
            )}
          </article>

          <aside className="screen-aside">
            <article className="surface-card screen-next-card">
              <p className="eyebrow">CONTINUE DAQUI</p>
              <h2>Fluxos relacionados</h2>
              <div className="screen-step-list">
                {nextScreens.map((item, index) => (
                  <Link className="screen-step" href={item.href} key={item.href}>
                    <span className="step-number">{String(index + 1).padStart(2, "0")}</span>
                    <span>{item.label}</span>
                    <Icon name="arrow-right" />
                  </Link>
                ))}
              </div>
            </article>
            <article className="screen-note-card">
              <span className="stat-icon stat-icon-yellow"><Icon name="shield" /></span>
              <p className="eyebrow">PREVISÃO DE PRODUTO</p>
              <h2>Seus dados ficam protegidos.</h2>
              <p>Esta interface não acessa dados do sistema antigo. A integração seguirá os contratos e a arquitetura do Applada.</p>
            </article>
          </aside>
        </section>

        <p className="demo-note">Correspondência do fluxo legado: {screen.legacyView}. O módulo visual está recriado; ações que exigem API permanecem demonstrativas.</p>
      </div>
    </AppShell>
  );
}
