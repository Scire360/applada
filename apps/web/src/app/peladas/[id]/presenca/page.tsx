import Link from "next/link";
import { notFound } from "next/navigation";
import { AppShell } from "@/components/app-shell";
import { AttendanceRoll } from "@/components/matches/attendance-roll";

export default async function AttendancePage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  if (!/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(id)) notFound();
  return (
    <AppShell active="matches" activePath="/peladas/conectadas" title="Chamada de presença" dataSource="api">
      <div className="module-page connected-page">
        <section className="page-heading">
          <h1>Chamada de presença</h1>
          <Link className="button button-secondary" href={`/peladas/conectadas?partida=${id}`}>Voltar à partida</Link>
        </section>
        <AttendanceRoll key={id} matchId={id} />
      </div>
    </AppShell>
  );
}
