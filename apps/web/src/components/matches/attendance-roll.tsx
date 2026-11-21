"use client";

import { useEffect, useRef, useState, useSyncExternalStore, type RefObject } from "react";
import { ApiError, appApi } from "@/lib/api-client";
import { attendanceMessage } from "@/lib/attendance";
import type { MatchDetails, Page } from "@/lib/connected-matches";
import { useAttendanceWindow } from "./use-attendance-window";
import { SignInAgain } from "../groups/sign-in-again";
import { useAction } from "../groups/use-action";

type Status = "present" | "absent";
type Mark = { userId: string; status: Status; recordedAt: string };
type Entry = { userId: string; displayName: string | null; status: Status | null; recordedAt: string | null };
type Start = Pick<MatchDetails, "startsAt" | "status">;
const labels = { present: "Veio", absent: "Faltou" } as const;

function onVisibilityChange(changed: () => void) {
  document.addEventListener("visibilitychange", changed);
  return () => document.removeEventListener("visibilitychange", changed);
}

export function AttendanceRoll({ matchId }: { matchId: string }) {
  const write = useRef<Promise<Mark> | null>(null);
  const [upcoming, setUpcoming] = useState<Start>();
  // Uma lista lida antes do apito ainda pode ganhar ou perder confirmados.
  const startedSinceRead = useAttendanceWindow(upcoming) === "started";
  // Ao voltar à aba, a sessão pode ser de outra pessoa. A nova montagem
  // precisa de um GET autorizado antes de exibir qualquer marcação.
  const visible = useSyncExternalStore(onVisibilityChange, () => document.visibilityState === "visible", () => false);
  return visible ? <VisibleRoll key={String(startedSinceRead)} matchId={matchId} write={write} onUpcoming={setUpcoming} /> : null;
}

function VisibleRoll({ matchId, write, onUpcoming }: {
  matchId: string;
  write: RefObject<Promise<Mark> | null>;
  onUpcoming: (match: Start) => void;
}) {
  const path = `matches/${matchId}/attendance`;
  const [page, setPage] = useState<Page<Entry>>();
  const [match, setMatch] = useState<MatchDetails>();
  const [pending, setPending] = useState<Pick<Mark, "userId" | "status">>();
  const [failure, setFailure] = useState<ApiError>();
  const [closedMessage, setClosedMessage] = useState("");
  const [attempt, setAttempt] = useState(0);
  const { busy, run, notice, setNotice } = useAction();
  const phase = useAttendanceWindow(match);

  useEffect(() => {
    const controller = new AbortController();
    const options = { signal: controller.signal };
    async function load() {
      try {
        // A aba pode ter sido ocultada durante um PUT da montagem anterior.
        await write.current?.catch(() => undefined);
        if (controller.signal.aborted) return;
        const requestedAt = Date.now();
        const next = await appApi<Page<Entry>>(path, options);
        const details = await appApi<MatchDetails>(`matches/${matchId}`, options);
        if (!controller.signal.aborted) {
          if (details.status === "scheduled" && requestedAt < Date.parse(details.startsAt)) {
            onUpcoming({ startsAt: details.startsAt, status: details.status });
          }
          setMatch(details);
          setPage(next);
        }
      } catch (error) {
        if (!controller.signal.aborted) setFailure(asApiError(error));
      }
    }
    void load();
    return () => controller.abort();
  }, [matchId, path, attempt, write, onUpcoming]);

  function more(cursor: string) {
    void run(async () => {
      setFailure(undefined);
      try {
        const next = await appApi<Page<Entry>>(`${path}?cursor=${encodeURIComponent(cursor)}`);
        setPage((current) => ({ items: [...(current?.items ?? []), ...next.items], nextCursor: next.nextCursor }));
      } catch (error) {
        setFailure(asApiError(error));
      }
    });
  }

  function mark(entry: Entry, status: Status) {
    void run(async () => {
      setFailure(undefined);
      setPending({ userId: entry.userId, status });
      try {
        write.current = appApi<Mark>(`${path}/${entry.userId}`, {
          method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ status }),
        });
        const saved = await write.current;
        setPage((current) => current && ({
          ...current, items: current.items.map((item) => item.userId === saved.userId ? { ...item, ...saved } : item),
        }));
        setNotice("Chamada atualizada.");
      } catch (error) {
        const problem = asApiError(error);
        setFailure(problem);
        if (problem.status === 409 && (problem.code === "attendance-closed" || problem.code === "match-cancelled")) {
          setClosedMessage(attendanceMessage(problem));
        }
      } finally {
        write.current = null;
        setPending(undefined);
      }
    });
  }

  const accessDenied = failure?.status === 401 || failure?.status === 403;
  const loading = !page && !failure;
  const windowMessage = closedMessage || (match?.status === "cancelled" ? "A partida foi cancelada."
    : phase === "not-open" ? "A chamada abre no início da partida." : phase === "closed" ? "A chamada fechou." : "");
  const nextCursor = page?.nextCursor;
  return (
    <section className="surface-card connected-card" aria-labelledby="confirmados">
      <h2 id="confirmados">Participantes confirmados</h2>
      <p role="status">{loading ? "Carregando chamada…" : busy ? "Aguarde…" : notice}</p>
      {failure && <p className="form-feedback form-feedback-error" role="alert">{attendanceMessage(failure)}</p>}
      {failure?.status === 401 && <SignInAgain />}
      {!page && failure && !accessDenied && (
        <button className="button button-secondary" type="button" onClick={() => { setFailure(undefined); setAttempt(attempt + 1); }}>Tentar novamente</button>
      )}
      {page && !accessDenied && (
        <>
          {windowMessage && (!failure || windowMessage !== attendanceMessage(failure)) && <p>{windowMessage}</p>}
          {page.items.length ? (
            <ul className="connected-list">
              {page.items.map((entry) => {
                const name = entry.displayName ?? entry.userId;
                const selected = pending?.userId === entry.userId ? pending.status : entry.status;
                return (
                  <li key={entry.userId}>
                    <strong>{name}</strong>
                    <div aria-label={`Presença de ${name}`} className="connected-actions" role="group">
                      {(["present", "absent"] as const).map((status) => (
                        <button key={status} aria-pressed={selected === status} className={`button button-${selected === status ? "primary" : "secondary"}`}
                          disabled={busy || Boolean(windowMessage)} onClick={() => mark(entry, status)} type="button">{labels[status]}</button>
                      ))}
                    </div>
                  </li>
                );
              })}
            </ul>
          ) : <p>Nenhum participante confirmado.</p>}
          {nextCursor && <button className="button button-secondary" disabled={busy} onClick={() => more(nextCursor)} type="button">Ver mais</button>}
        </>
      )}
    </section>
  );
}

function asApiError(error: unknown) {
  return error instanceof ApiError ? error : new ApiError(0, "Não foi possível carregar ou atualizar a chamada.");
}
