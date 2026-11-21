"use client";

import Link from "next/link";
import { Component, createRef, useEffect, useState } from "react";
import { appApi } from "@/lib/api-client";
import { attendanceFocusTarget, attendanceLinkAction, isAttendanceRecheck } from "@/lib/attendance";
import type { MatchDetails, MatchSummary } from "@/lib/connected-matches";
import { useAttendanceWindow } from "./use-attendance-window";

export function AttendanceLink({ match, userId, rowTargetId, knownOrganizer = false }: {
  match: MatchSummary | MatchDetails;
  userId: string;
  rowTargetId: string;
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
  const checkingAgain = action === "load" && isAttendanceRecheck(match.id, attempt, failure);

  useEffect(() => {
    if (action !== "load") return;
    const controller = new AbortController();
    async function check() {
      try {
        const next = await appApi<MatchDetails>(`matches/${match.id}`, { signal: controller.signal });
        if (controller.signal.aborted) return;
        setDetails(next);
      } catch (error) {
        if (controller.signal.aborted) return;
        setFailure({ matchId: match.id, attempt, error: error ?? null });
      }
    }
    void check();
    return () => controller.abort();
  }, [match.id, action, attempt, userId]);

  useEffect(() => {
    if (action !== "retry") return;
    const retry = () => setAttempt(value => value + 1);
    window.addEventListener("online", retry, { once: true });
    return () => window.removeEventListener("online", retry);
  }, [action]);

  return <AttendanceLinkControl action={action} checkingAgain={checkingAgain} match={match} rowTargetId={rowTargetId}
    onRetry={() => setAttempt(value => value + 1)} />;
}

type ControlProps = {
  action: ReturnType<typeof attendanceLinkAction>;
  checkingAgain: boolean;
  match: Pick<MatchSummary, "id" | "title">;
  rowTargetId: string;
  onRetry: () => void;
};

class AttendanceLinkControl extends Component<ControlProps> {
  private retryButton = createRef<HTMLButtonElement>();
  private link = createRef<HTMLAnchorElement>();

  getSnapshotBeforeUpdate() {
    const button = this.retryButton.current;
    const target = attendanceFocusTarget(button !== null && document.activeElement === button, this.props.action);
    if (target === "row") document.getElementById(this.props.rowTargetId)?.focus();
    return target;
  }

  componentDidUpdate(_prevProps: ControlProps, _prevState: unknown, target: ReturnType<typeof attendanceFocusTarget>) {
    if (target === "link") this.link.current?.focus();
  }

  render() {
    const { action, checkingAgain, match, onRetry } = this.props;
    if (action === "hidden") return null;
    if (action === "link") {
      return <Link className="button button-secondary" href={`/peladas/${match.id}/presenca`} ref={this.link}>Fazer chamada</Link>;
    }
    return (
      <div className="attendance-link-feedback form-feedback form-feedback-error" role="status">
        {(action === "retry" || checkingAgain) && <>
          {!checkingAgain && <span>Não foi possível verificar a chamada.</span>}
          <button className="button button-secondary" type="button" ref={this.retryButton} aria-disabled={checkingAgain}
            aria-label={checkingAgain ? undefined : `Tentar de novo a consulta da chamada de ${match.title}`}
            onClick={() => { if (!checkingAgain) onRetry(); }}>
            {checkingAgain ? "Verificando…" : "Tentar de novo"}
          </button>
        </>}
      </div>
    );
  }
}
