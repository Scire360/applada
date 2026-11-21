import { AppShell } from "@/components/app-shell";
import { OpenMatches } from "@/components/discovery/open-matches";

export default function OpenMatchesPage() {
  return (
    <AppShell active="matches" activePath="/peladas/abertas" title="Peladas abertas" dataSource="api">
      <div className="module-page connected-page">
        <header className="page-heading">
          <div>
            <h1>Peladas abertas</h1>
            <p className="page-subtitle">Encontre sua próxima partida pela distância até a quadra ou o campo.</p>
          </div>
        </header>
        <OpenMatches />
      </div>
    </AppShell>
  );
}
