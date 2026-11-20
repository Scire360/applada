import Link from "next/link";
import { AppShell } from "@/components/app-shell";
import { ConnectedMatches } from "@/components/connected-matches";
import { getApiMode } from "@/lib/api-mode";

export default function ConnectedMatchesPage() {
  const apiMode = getApiMode();
  return (
    <AppShell
      active="matches"
      activePath="/peladas/conectadas"
      title="Partidas conectadas"
      variant={apiMode === "local" || apiMode === "preview" ? "local" : undefined}
    >
      <div className="module-page connected-page">
        <section className="page-heading">
          <div>
            <h1>Partidas conectadas</h1>
            <p className="page-subtitle">Organize a próxima pelada, compartilhe o link e acompanhe as inscrições.</p>
          </div>
          <Link className="button button-secondary" href="/peladas">Voltar à agenda</Link>
        </section>
        <ConnectedMatches apiMode={apiMode === "preview" ? "local" : apiMode} />
      </div>
    </AppShell>
  );
}
