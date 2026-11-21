import { AppShell } from "@/components/app-shell";
import { MyGroups } from "@/components/groups/my-groups";

export default function GroupsPage() {
  return (
    <AppShell active="groups" activePath="/grupos" title="Grupos" dataSource="api">
      <div className="module-page connected-page">
        <section className="page-heading">
          <div>
            <h1>Grupos</h1>
            <p className="page-subtitle">Reúna quem joga com você, convide pessoas por link e marque partidas para o grupo.</p>
          </div>
        </section>
        <MyGroups />
      </div>
    </AppShell>
  );
}
