"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ApiError, appApi } from "@/lib/api-client";
import type { MatchDetails, MatchSummary } from "@/lib/connected-matches";
import { useAttendanceWindow } from "./use-attendance-window";

export function AttendanceLink({ match, userId }: { match: MatchSummary | MatchDetails; userId: string }) {
  const [details, setDetails] = useState<MatchDetails>();
  const current = details?.id === match.id ? details : undefined;
  const phase = useAttendanceWindow(current ?? match);
  const started = match.status === "scheduled" && (phase === "started" || phase === "open");

  // O resumo não traz organizador nem duração. Busca o detalhe após o início,
  // quando já não é possível outro usuário editar ou cancelar a partida.
  useEffect(() => {
    if (!started) return;
    const controller = new AbortController();
    let retry: number;
    let loading = false;
    async function check() {
      if (loading) return;
      window.clearTimeout(retry);
      loading = true;
      try {
        const next = await appApi<MatchDetails>(`matches/${match.id}`, { signal: controller.signal });
        if (!controller.signal.aborted) setDetails(next);
      } catch (error) {
        if (!controller.signal.aborted) {
          setDetails(undefined);
          if (!(error instanceof ApiError) || error.status === 0 || error.status >= 500) retry = window.setTimeout(check, 15_000);
        }
      } finally {
        loading = false;
      }
    }
    void check();
    window.addEventListener("online", check);
    return () => {
      controller.abort();
      window.clearTimeout(retry);
      window.removeEventListener("online", check);
    };
  }, [match.id, started]);

  if (!started || phase !== "open" || current?.organizerId !== userId) return null;
  return <Link className="button button-secondary" href={`/peladas/${match.id}/presenca`}>Fazer chamada</Link>;
}
