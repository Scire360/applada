import Link from "next/link";
import { AppShell } from "@/components/app-shell";
import { ConnectedMatches } from "@/components/connected-matches";

export default function ConnectedMatchesPage() {
  return (
    <AppShell active="matches" activePath="/peladas/conectadas" title="Partidas conectadas" dataSource="api">
      <div className="module-page connected-page">
        <section className="page-heading">
          <div>
            <h1>Partidas conectadas</h1>
            <p className="page-subtitle">Organize a próxima pelada, compartilhe o link e acompanhe as inscrições.</p>
          </div>
          <Link className="button button-secondary" href="/peladas">Voltar à agenda demonstrativa</Link>
        </section>
        <ConnectedMatches />
      </div>
    </AppShell>
  );
}
