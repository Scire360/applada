import { AppShell } from "@/components/app-shell";
import { GroupDetails } from "@/components/groups/group-details";

export default async function GroupPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return (
    <AppShell active="groups" activePath="/grupos" title="Grupo" dataSource="api">
      <div className="module-page connected-page">
        <GroupDetails id={id} />
      </div>
    </AppShell>
  );
}
