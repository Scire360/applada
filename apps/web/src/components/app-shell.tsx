import Link from "next/link";
import type { ReactNode } from "react";
import { Icon } from "@/components/icon";
import type { ScreenGroup } from "@/lib/screens";
import { HeaderAccountButton, SidebarAccountButton } from "./account-dialog";

type NavigationItem = {
  href: string;
  label: string;
  icon: "home" | "football" | "people" | "sparkles" | "wallet" | "calendar" | "image" | "bell" | "trophy" | "chart" | "file" | "settings" | "shield" | "search" | "plus";
  key: string;
  primary?: boolean;
  count?: string;
};

const primaryNavigation: NavigationItem[] = [
  { href: "/", label: "Visão geral", icon: "home", key: "overview", primary: true },
  { href: "/peladas", label: "Peladas", icon: "football", key: "matches", primary: true, count: "3" },
  { href: "/jogadores", label: "Jogadores", icon: "people", key: "players", primary: true },
  { href: "/comunidade", label: "Comunidade", icon: "sparkles", key: "community", primary: true },
  { href: "/financeiro", label: "Financeiro", icon: "wallet", key: "finance", primary: true },
];

const navigationSections: { title: string; links: NavigationItem[] }[] = [
  {
    title: "Dentro de campo",
    links: [
      { href: "/eventos/novo", label: "Organizar pelada", icon: "plus", key: "events-new" },
      { href: "/peladas/abertas", label: "Peladas abertas", icon: "search", key: "open-matches" },
      { href: "/presenca", label: "Presença", icon: "calendar", key: "presence-center" },
      { href: "/presenca/check-in", label: "Check-in", icon: "people", key: "checkin-center" },
      { href: "/partidas/ao-vivo", label: "Central da partida", icon: "football", key: "match-center" },
      { href: "/jogadores/perfil", label: "Perfil do jogador", icon: "people", key: "player-profile" },
    ],
  },
  {
    title: "Comunidade",
    links: [
      { href: "/galeria", label: "Galeria", icon: "image", key: "gallery-center" },
      { href: "/aniversarios", label: "Aniversários", icon: "calendar", key: "birthdays-center" },
      { href: "/notificacoes", label: "Notificações", icon: "bell", key: "notifications-center" },
      { href: "/ofertas", label: "Benefícios", icon: "sparkles", key: "offers-center" },
      { href: "/destaques/votacao", label: "Destaques", icon: "trophy", key: "highlight-vote" },
    ],
  },
  {
    title: "Acompanhamento",
    links: [
      { href: "/financeiro/lancamentos", label: "Lançamentos", icon: "wallet", key: "finance-entries" },
      { href: "/financeiro/cobrancas", label: "Contribuições", icon: "wallet", key: "finance-collections" },
      { href: "/financeiro/categorias", label: "Categorias", icon: "file", key: "finance-category-list" },
      { href: "/desempenho", label: "Desempenho", icon: "chart", key: "performance-center" },
      { href: "/relatorios", label: "Relatórios", icon: "file", key: "reports-center" },
    ],
  },
  {
    title: "Espaço do grupo",
    links: [
      { href: "/administracao", label: "Administração", icon: "settings", key: "admin-center" },
      { href: "/administracao/usuarios", label: "Pessoas e acessos", icon: "people", key: "users-list" },
      { href: "/administracao/organizacoes", label: "Espaços e grupos", icon: "shield", key: "organizations-center" },
      { href: "/administracao/configuracoes", label: "Configurações", icon: "settings", key: "settings-center" },
    ],
  },
  {
    title: "Ferramentas",
    links: [
      { href: "/buscar", label: "Buscar no Applada", icon: "search", key: "search" },
      { href: "/modulos", label: "Mapa de funcionalidades", icon: "file", key: "catalog" },
    ],
  },
];

type AppShellProps = {
  active: "overview" | ScreenGroup;
  activePath?: string;
  title?: string;
  variant?: "local";
  children: ReactNode;
};

function NavigationSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <details className="nav-section">
      <summary className="nav-heading"><span>{title}</span><Icon name="chevron-down" /></summary>
      <div className="nav-section-links">{children}</div>
    </details>
  );
}

export function AppShell({ active, activePath, title, variant, children }: AppShellProps) {
  const primaryLinks = primaryNavigation.map((item) => {
    const isActive = item.href === "/"
      ? active === "overview"
      : item.key === active || activePath === item.href;
    return (
      <Link
        aria-current={(activePath ? activePath === item.href : isActive) ? "page" : undefined}
        aria-label={item.label}
        className={`nav-link nav-link-primary${isActive ? " is-active" : ""}`}
        href={item.href}
        key={item.key}
        title={item.label}
      >
        <Icon name={item.icon} />
        <span>{item.label}</span>
        {item.count ? <span aria-hidden="true" className="nav-count">{item.count}</span> : null}
      </Link>
    );
  });

  return (
    <div className="app-shell">
      <a className="skip-link" href="#conteudo">Pular para o conteúdo</a>
      <aside className="sidebar">
        <Link aria-label="Applada — início" className="brand" href="/">
          <span className="brand-mark">
            <Icon name="football" />
          </span>
          <span className="brand-wordmark">applada</span>
        </Link>

        <div className="workspace-switcher">
          <span aria-hidden="true" className="workspace-avatar">{variant === "local" ? "AP" : "SC"}</span>
          <span className="workspace-copy">
            <span className="eyebrow">SEU ESPAÇO</span>
            <span className="workspace-name">{variant === "local" ? "Testes do Applada" : "Santa Cecília FC"}</span>
          </span>
        </div>

        <nav aria-label="Navegação principal" className="primary-nav">
          <NavigationSection title="Menu">{primaryLinks}</NavigationSection>
          {navigationSections.map((section) => (
            <NavigationSection title={section.title} key={section.title}>
              {section.links.map((item) => (
                <Link
                  aria-current={activePath === item.href ? "page" : undefined}
                  className={`nav-link nav-link-secondary${activePath === item.href ? " is-active" : ""}`}
                  href={item.href}
                  key={item.key}
                >
                  <Icon name={item.icon} />
                  <span>{item.label}</span>
                </Link>
              ))}
            </NavigationSection>
          ))}
          <div className="compact-primary-nav">{primaryLinks}</div>
        </nav>

        {!variant && <div className="sidebar-bottom">
          <div className="sidebar-season">
            <span className="season-icon">
              <Icon name="sparkles" />
            </span>
            <span>
              <strong>Jogue junto</strong>
              <span>O próximo jogo começa aqui.</span>
            </span>
          </div>
          <div className="sidebar-profile">
            <SidebarAccountButton />
          </div>
        </div>}
      </aside>

      <div className="main-column">
        <header className="topbar">
          <div aria-label={`Você está em ${title ?? (active === "overview" ? "Visão geral" : "seu espaço")}`} className="breadcrumbs">
            <span>{variant === "local" ? "Applada" : "Santa Cecília FC"}</span>
            <span className="breadcrumb-divider">/</span>
            <strong>{title ?? (active === "overview" ? "Visão geral" : "Seu espaço")}</strong>
          </div>
          <div className="topbar-meta">
            <Link aria-current={activePath === "/modulos" ? "page" : undefined} aria-label="Mapa de funcionalidades" className="compact-catalog-link" href="/modulos" title="Mapa de funcionalidades">
              <Icon name="file" />
            </Link>
            <span className="preview-status">
              <span className="status-dot" />
              {variant === "local" ? "Ambiente local" : "Prévia · dados ilustrativos"}
            </span>
            <HeaderAccountButton />
          </div>
        </header>
        <main className="page-content" id="conteudo" tabIndex={-1}>{children}</main>
      </div>
    </div>
  );
}
