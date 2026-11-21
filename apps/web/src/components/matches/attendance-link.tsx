"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { appApi } from "@/lib/api-client";
import type { MatchDetails, MatchSummary } from "@/lib/connected-matches";
import { useAttendanceWindow } from "./use-attendance-window";

export function AttendanceLink({ match, userId }: { match: MatchSummary | MatchDetails; userId: string }) {
  const [details, setDetails] = useState<MatchDetails>();
  const current = "organizerId" in match && "durationMinutes" in match ? match : details?.id === match.id ? details : undefined;
  const phase = useAttendanceWindow(current ?? match);
  const started = match.status === "scheduled" && (phase === "started" || phase === "open");

  // O resumo não traz organizador nem duração. Busca o detalhe após o início,
  // quando já não é possível outro usuário editar ou cancelar a partida.
  useEffect(() => {
    if (!started || current) return;
    const controller = new AbortController();
    async function check() {
      try {
        const next = await appApi<MatchDetails>(`matches/${match.id}`, { signal: controller.signal });
        if (!controller.signal.aborted) setDetails(next);
      } catch {
        if (!controller.signal.aborted) window.addEventListener("online", check, { once: true });
      }
    }
    void check();
    return () => {
      controller.abort();
      window.removeEventListener("online", check);
    };
  }, [match.id, started, current]);

  if (!started || phase !== "open" || current?.organizerId !== userId) return null;
  return <Link className="button button-secondary" href={`/peladas/${match.id}/presenca`}>Fazer chamada</Link>;
}
