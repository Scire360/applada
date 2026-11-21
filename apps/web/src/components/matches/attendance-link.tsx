"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { appApi } from "@/lib/api-client";
import { attendanceLinkAction } from "@/lib/attendance";
import type { MatchDetails, MatchSummary } from "@/lib/connected-matches";
import { useAttendanceWindow } from "./use-attendance-window";

export function AttendanceLink({ match, userId, knownOrganizer = false }: {
  match: MatchSummary | MatchDetails;
  userId: string;
  knownOrganizer?: boolean;
}) {
  const [details, setDetails] = useState<MatchDetails>();
  const [attempt, setAttempt] = useState(0);
  const [failure, setFailure] = useState<{ matchId: string; attempt: number; error: unknown }>();
  const current = "organizerId" in match ? match : details?.id === match.id ? details : undefined;
  const phase = useAttendanceWindow(current ?? match);
  const isOrganizer = knownOrganizer || (current ? current.organizerId === userId : undefined);
  const currentError = failure?.matchId === match.id && failure.attempt === attempt ? failure.error : undefined;
  const action = attendanceLinkAction(phase, isOrganizer, currentError);

  useEffect(() => {
    if (action !== "load") return;
    const controller = new AbortController();
    async function check() {
      try {
        const next = await appApi<MatchDetails>(`matches/${match.id}`, { signal: controller.signal });
        if (!controller.signal.aborted) setDetails(next);
      } catch (error) {
        if (controller.signal.aborted) return;
        setFailure({ matchId: match.id, attempt, error: error ?? null });
      }
    }
    void check();
    return () => controller.abort();
  }, [match.id, action, attempt]);

  useEffect(() => {
    if (action !== "retry") return;
    const retry = () => setAttempt(value => value + 1);
    window.addEventListener("online", retry, { once: true });
    return () => window.removeEventListener("online", retry);
  }, [action]);

  if (action === "load" || action === "retry") {
    return (
      <div className="attendance-link-feedback form-feedback-error" role="status">
        {action === "retry" && <>
          <span>Não foi possível verificar a chamada.</span>
          <button className="button button-secondary" type="button" aria-label={`Tentar de novo a consulta da chamada de ${match.title}`}
            onClick={() => setAttempt(value => value + 1)}>Tentar de novo</button>
        </>}
      </div>
    );
  }
  if (action !== "link") return null;
  return <Link className="button button-secondary" href={`/peladas/${match.id}/presenca`}>Fazer chamada</Link>;
}
