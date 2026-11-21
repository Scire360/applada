"use client";

import { useEffect, useState } from "react";
import { ApiError, appApi } from "@/lib/api-client";
import { accessDenied, attendanceMessage, attendanceProblem, readAttendance } from "@/lib/attendance";
import type { MatchDetails, Page } from "@/lib/connected-matches";
import { useAttendanceWindow } from "./use-attendance-window";
import { SignInAgain } from "../groups/sign-in-again";
import { useAction } from "../groups/use-action";

type Status = "present" | "absent";
type Mark = { userId: string; status: Status; recordedAt: string };
type Entry = { userId: string; displayName: string | null; status: Status | null; recordedAt: string | null };
const labels = { present: "Veio", absent: "Faltou" } as const;

export function AttendanceRoll({ matchId }: { matchId: string }) {
  const path = `matches/${matchId}/attendance`;
  const [page, setPage] = useState<Page<Entry>>();
  const [match, setMatch] = useState<MatchDetails>();
  const [pending, setPending] = useState<Pick<Mark, "userId" | "status">>();
  const [failure, setFailure] = useState<ApiError>();
  const [closedMessage, setClosedMessage] = useState("");
  const [readBeforeStart, setReadBeforeStart] = useState(false);
  const [loadFailure, setLoadFailure] = useState<ApiError>();
  const { busy, run, notice, setNotice } = useAction();
  const phase = useAttendanceWindow(match);
  // Uma leitura iniciada antes do apito ainda pode ter confirmados desatualizados.
  const needsLoad = !page || (phase === "open" && readBeforeStart);

  useEffect(() => {
    if (!needsLoad || loadFailure || busy) return;
    const controller = new AbortController();
    const options = { signal: controller.signal };
    async function load() {
      try {
        const requestedAt = Date.now();
        const [next, details] = await readAttendance(
          appApi<Page<Entry>>(path, options),
          appApi<MatchDetails>(`matches/${matchId}`, options),
        );
        if (!controller.signal.aborted) {
          setFailure(undefined);
          setReadBeforeStart(requestedAt < Date.parse(details.startsAt));
          setMatch(details);
          setPage(next);
          if (readBeforeStart) setNotice("Lista atualizada no início da partida.");
        }
      } catch (error) {
        if (!controller.signal.aborted) setLoadFailure(asApiError(error));
      }
    }
    void load();
    return () => controller.abort();
  }, [matchId, path, needsLoad, loadFailure, busy, readBeforeStart, setNotice]);

  useEffect(() => {
    let current: AbortController | undefined;
    // A sessão pode mudar em outra aba. Revalida o acesso sem perder a paginação nem interferir no PUT.
    async function checkAccess() {
      if (document.visibilityState !== "visible") return;
      current?.abort();
      const controller = new AbortController();
      current = controller;
      try {
        await appApi(`${path}?limit=1`, { signal: controller.signal });
        if (!controller.signal.aborted) {
          setLoadFailure((error) => accessDenied(error) ? undefined : error);
          setFailure((error) => accessDenied(error) ? undefined : error);
        }
      } catch (error) {
        if (!controller.signal.aborted && error instanceof ApiError && accessDenied(error)) {
          setLoadFailure(error);
        }
      }
    }
    document.addEventListener("visibilitychange", checkAccess);
    return () => {
      current?.abort();
      document.removeEventListener("visibilitychange", checkAccess);
    };
  }, [path]);

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
        const saved = await appApi<Mark>(`${path}/${entry.userId}`, {
          method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ status }),
        });
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
        setPending(undefined);
      }
    });
  }

  const problem = attendanceProblem(loadFailure, failure);
  const loading = needsLoad && !loadFailure;
  const windowMessage = closedMessage || (match?.status === "cancelled" ? "A partida foi cancelada."
    : phase === "not-open" ? "A chamada abre no início da partida." : phase === "closed" ? "A chamada fechou." : "");
  const nextCursor = page?.nextCursor;
  return (
    <section className="surface-card connected-card" aria-labelledby="confirmados">
      <h2 id="confirmados">Participantes confirmados</h2>
      <p role="status">{loading ? "Carregando chamada…" : busy ? "Aguarde…" : notice}</p>
      {problem && <p className="form-feedback form-feedback-error" role="alert">{attendanceMessage(problem)}</p>}
      {problem?.status === 401 && <SignInAgain />}
      {needsLoad && loadFailure && !accessDenied(problem) && (
        <button className="button button-secondary" type="button" onClick={() => setLoadFailure(undefined)}>Tentar novamente</button>
      )}
      {page && !accessDenied(problem) && (
        <>
          {windowMessage && (!problem || windowMessage !== attendanceMessage(problem)) && <p>{windowMessage}</p>}
          {page.items.length ? (
            <ul className="connected-list">
              {page.items.map((entry) => {
                const name = entry.displayName ?? "Jogador sem nome";
                const selected = pending?.userId === entry.userId ? pending.status : entry.status;
                return (
                  <li key={entry.userId}>
                    <strong>{name}</strong>
                    <div aria-label={`Presença de ${name}`} className="connected-actions" role="group">
                      {(["present", "absent"] as const).map((status) => (
                        <button key={status} aria-pressed={selected === status} className={`button button-${selected === status ? "primary" : "secondary"}`}
                          disabled={busy || needsLoad || Boolean(windowMessage)} onClick={() => mark(entry, status)} type="button">{labels[status]}</button>
                      ))}
                    </div>
                  </li>
                );
              })}
            </ul>
          ) : <p>Nenhum participante confirmado.</p>}
          {nextCursor && <button className="button button-secondary" disabled={busy || needsLoad} onClick={() => more(nextCursor)} type="button">Ver mais</button>}
        </>
      )}
    </section>
  );
}

function asApiError(error: unknown) {
  return error instanceof ApiError ? error : new ApiError(0, "Não foi possível carregar ou atualizar a chamada.");
}
