import Link from "next/link";
import type { ReactNode } from "react";
import { Icon } from "@/components/icon";
import type { ScreenGroup } from "@/lib/screens";

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
    title: "DENTRO DE CAMPO",
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
    title: "COMUNIDADE",
    links: [
      { href: "/galeria", label: "Galeria", icon: "image", key: "gallery-center" },
      { href: "/aniversarios", label: "Aniversários", icon: "calendar", key: "birthdays-center" },
      { href: "/notificacoes", label: "Notificações", icon: "bell", key: "notifications-center" },
      { href: "/ofertas", label: "Benefícios", icon: "sparkles", key: "offers-center" },
      { href: "/destaques/votacao", label: "Destaques", icon: "trophy", key: "highlight-vote" },
    ],
  },
  {
    title: "ACOMPANHAMENTO",
    links: [
      { href: "/financeiro/lancamentos", label: "Lançamentos", icon: "wallet", key: "finance-entries" },
      { href: "/financeiro/cobrancas", label: "Contribuições", icon: "wallet", key: "finance-collections" },
      { href: "/financeiro/categorias", label: "Categorias", icon: "file", key: "finance-category-list" },
      { href: "/desempenho", label: "Desempenho", icon: "chart", key: "performance-center" },
      { href: "/relatorios", label: "Relatórios", icon: "file", key: "reports-center" },
    ],
  },
  {
    title: "ESPAÇO DO GRUPO",
    links: [
      { href: "/administracao", label: "Administração", icon: "settings", key: "admin-center" },
      { href: "/administracao/usuarios", label: "Pessoas e acessos", icon: "people", key: "users-list" },
      { href: "/administracao/organizacoes", label: "Espaços e grupos", icon: "shield", key: "organizations-center" },
      { href: "/administracao/configuracoes", label: "Configurações", icon: "settings", key: "settings-center" },
    ],
  },
];

type AppShellProps = {
  active: "overview" | ScreenGroup;
  activePath?: string;
  title?: string;
  children: ReactNode;
};

export function AppShell({ active, activePath, title, children }: AppShellProps) {
  return (
    <div className="app-shell">
      <aside className="sidebar">
        <Link aria-label="Applada — início" className="brand" href="/">
          <span className="brand-mark">
            <Icon name="football" />
          </span>
          <span className="brand-wordmark">applada</span>
        </Link>

        <div className="workspace-switcher">
          <span className="workspace-avatar">SC</span>
          <span className="workspace-copy">
            <span className="eyebrow">SEU ESPAÇO</span>
            <span className="workspace-name">Santa Cecília FC</span>
          </span>
          <Icon className="workspace-chevron" name="chevron-down" />
        </div>

        <nav aria-label="Navegação principal" className="primary-nav">
          <p className="nav-heading">MENU</p>
          {primaryNavigation.map((item) => {
            const isActive = item.href === "/"
              ? active === "overview"
              : item.key === active || activePath === item.href;
            return (
              <Link
                aria-current={item.href === "/"
                  ? active === "overview" ? "page" : undefined
                  : activePath === item.href ? "page" : undefined}
                className={`nav-link nav-link-primary${isActive ? " is-active" : ""}`}
                href={item.href}
                key={item.key}
              >
                <Icon name={item.icon} />
                <span>{item.label}</span>
                {item.count ? <span className="nav-count">{item.count}</span> : null}
              </Link>
            );
          })}
          {navigationSections.map((section) => (
            <details
              className="nav-section"
              key={section.title}
              open={section.links.some((item) => activePath === item.href)}
            >
              <summary className="nav-heading">{section.title}</summary>
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
            </details>
          ))}
          <details
            className="nav-section nav-tools"
            open={activePath === "/buscar" || activePath === "/modulos"}
          >
            <summary className="nav-heading">FERRAMENTAS</summary>
            <Link aria-current={activePath === "/buscar" ? "page" : undefined} className={`nav-link nav-link-secondary${activePath === "/buscar" ? " is-active" : ""}`} href="/buscar">
              <Icon name="search" />
              <span>Buscar no Applada</span>
            </Link>
            <Link aria-current={activePath === "/modulos" ? "page" : undefined} className={`nav-link nav-link-secondary${activePath === "/modulos" ? " is-active" : ""}`} href="/modulos">
              <Icon name="file" />
              <span>Mapa de funcionalidades</span>
            </Link>
          </details>
        </nav>

        <div className="sidebar-bottom">
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
            <Link aria-label="Abrir perfil de Rafael Martins" className="sidebar-profile-link" href="/jogadores/perfil">
              <span className="avatar avatar-dark">RM</span>
              <span className="profile-copy">
                <strong>Rafael Martins</strong>
                <span>Organizador</span>
              </span>
              <Icon className="profile-chevron" name="chevron-down" />
            </Link>
          </div>
        </div>
      </aside>

      <div className="main-column">
        <header className="topbar">
          <div aria-label={`Você está em ${title ?? (active === "overview" ? "Visão geral" : "seu espaço")}`} className="breadcrumbs">
            <span>Santa Cecília FC</span>
            <span className="breadcrumb-divider">/</span>
            <strong>{title ?? (active === "overview" ? "Visão geral" : "Seu espaço")}</strong>
          </div>
          <div className="topbar-meta">
            <span className="preview-status">
              <span className="status-dot" />
              Prévia · dados ilustrativos
            </span>
            <span aria-label="Usuário Rafael Martins" className="avatar avatar-light">
              RM
            </span>
          </div>
        </header>
        <main className="page-content">{children}</main>
      </div>
    </div>
  );
}
