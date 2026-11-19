import Link from "next/link";
import { Suspense, type ReactNode } from "react";
import { headers } from "next/headers";
import { connection } from "next/server";
import { Icon } from "@/components/icon";
import { signOut } from "@/app/login/actions";
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

export function AppShell(props: AppShellProps) {
  return (
    <Suspense fallback={<div className="screen-loading" role="status">Carregando seu espaço…</div>}>
      <AuthenticatedAppShell {...props} />
    </Suspense>
  );
}

async function AuthenticatedAppShell({ active, activePath, title, children }: AppShellProps) {
  await connection();
  const requestHeaders = await headers();
  const accountEmail = requestHeaders.get("x-applada-auth-email") || "Membro Applada";
  const accountInitials = accountEmail
    .split("@")[0]
    .split(/[.\s_-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? "")
    .join("") || "AP";

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
                aria-current={isActive ? "page" : undefined}
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
            <div className="nav-section" key={section.title}>
              <p className="nav-heading">{section.title}</p>
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
            </div>
          ))}
          <div className="nav-section nav-tools">
            <p className="nav-heading">FERRAMENTAS</p>
            <Link aria-current={activePath === "/buscar" ? "page" : undefined} className={`nav-link nav-link-secondary${activePath === "/buscar" ? " is-active" : ""}`} href="/buscar">
              <Icon name="search" />
              <span>Buscar no Applada</span>
            </Link>
            <Link aria-current={activePath === "/modulos" ? "page" : undefined} className={`nav-link nav-link-secondary${activePath === "/modulos" ? " is-active" : ""}`} href="/modulos">
              <Icon name="file" />
              <span>Mapa de funcionalidades</span>
            </Link>
          </div>
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
            <div className="sidebar-profile-info">
              <span aria-hidden="true" className="avatar avatar-dark">{accountInitials}</span>
              <span className="profile-copy">
                <strong title={accountEmail}>{accountEmail}</strong>
                <span>Conta autenticada</span>
              </span>
            </div>
            <form action={signOut}>
              <button className="sidebar-signout" type="submit">Sair da conta</button>
            </form>
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
            <span aria-label={`Conta autenticada: ${accountEmail}`} className="avatar avatar-light">
              {accountInitials}
            </span>
          </div>
        </header>
        <main className="page-content">{children}</main>
      </div>
    </div>
  );
}
