"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { appApi } from "@/lib/api-client";
import type { MatchDetails, MatchSummary } from "@/lib/connected-matches";
import { useAttendanceWindow } from "./use-attendance-window";

export function AttendanceLink({ match, userId, knownOrganizer = false }: {
  match: MatchSummary | MatchDetails;
  userId: string;
  knownOrganizer?: boolean;
}) {
  const [details, setDetails] = useState<MatchDetails>();
  const [attempt, setAttempt] = useState(0);
  const [failure, setFailure] = useState<{ matchId: string; attempt: number }>();
  const current = "organizerId" in match ? match : details?.id === match.id ? details : undefined;
  const phase = useAttendanceWindow(current ?? match);
  const needsDetails = phase === "open" && !knownOrganizer && !current;

  useEffect(() => {
    if (!needsDetails) return;
    const controller = new AbortController();
    const retry = () => setAttempt(value => value + 1);
    async function check() {
      try {
        const next = await appApi<MatchDetails>(`matches/${match.id}`, { signal: controller.signal });
        if (!controller.signal.aborted) setDetails(next);
      } catch {
        if (controller.signal.aborted) return;
        setFailure({ matchId: match.id, attempt });
        window.addEventListener("online", retry, { once: true });
      }
    }
    void check();
    return () => {
      controller.abort();
      window.removeEventListener("online", retry);
    };
  }, [match.id, needsDetails, attempt]);

  if (needsDetails && failure?.matchId === match.id && failure.attempt === attempt) {
    return <button className="button button-secondary" type="button" aria-label={`Tentar de novo a consulta da chamada de ${match.title}`}
      onClick={() => setAttempt(value => value + 1)}>Tentar de novo</button>;
  }
  if (phase !== "open" || (!knownOrganizer && current?.organizerId !== userId)) return null;
  return <Link className="button button-secondary" href={`/peladas/${match.id}/presenca`}>Fazer chamada</Link>;
}
