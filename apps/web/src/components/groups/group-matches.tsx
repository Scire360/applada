"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { appApi } from "@/lib/api-client";
import { matchDate, type MatchSummary, type Page } from "@/lib/connected-matches";
import { canManageInvitations, type Role } from "@/lib/groups";
import { useAction } from "./use-action";

function listPath(groupId: string, cursor?: string | null) {
  return `groups/${groupId}/matches${cursor ? `?cursor=${encodeURIComponent(cursor)}` : ""}`;
}

export function GroupMatches({ groupId, myRole }: { groupId: string; myRole: Role }) {
  const [page, setPage] = useState<Page<MatchSummary>>();
  const [loadError, setLoadError] = useState("");
  const { busy, error, run } = useAction();

  useEffect(() => {
    const controller = new AbortController();
    appApi<Page<MatchSummary>>(listPath(groupId), { signal: controller.signal })
      .then(setPage)
      .catch((failure: unknown) => {
        if (!controller.signal.aborted) setLoadError(failure instanceof Error ? failure.message : "Não foi possível carregar as partidas.");
      });
    return () => controller.abort();
  }, [groupId]);

  function more() {
    if (!page?.nextCursor) return;
    const cursor = page.nextCursor;
    void run(async () => {
      const next = await appApi<Page<MatchSummary>>(listPath(groupId, cursor));
      setPage((current) => ({ items: [...(current?.items ?? []), ...next.items], nextCursor: next.nextCursor }));
    });
  }

  return (
    <section className="surface-card connected-card" aria-labelledby="proximas-partidas" aria-busy={!page && !loadError}>
      <h2 id="proximas-partidas">Próximas partidas</h2>
      {!page && !loadError && <p role="status">Carregando partidas…</p>}
      {(loadError || error) && <p className="form-feedback form-feedback-error" role="alert">{loadError || error}</p>}
      {page && (page.items.length ? (
        <ul className="connected-list">
          {page.items.map((match) => (
            <li key={match.id}>
              <Link className="connected-match-item group-link" href={`/peladas/conectadas?partida=${match.id}`}>
                <strong>{match.title}</strong>
                <span>{matchDate(match.startsAt)} · {match.status === "cancelled" ? "Cancelada" : `${match.confirmedCount}/${match.capacity} vagas`}</span>
              </Link>
            </li>
          ))}
        </ul>
      ) : <p>Nenhuma partida marcada para este grupo.</p>)}
      {page?.nextCursor && <button className="button button-secondary" disabled={busy} onClick={more} type="button">Mais partidas</button>}
      {canManageInvitations(myRole) && (
        <Link className="button button-primary" href={`/peladas/conectadas?grupo=${groupId}`}>Criar partida para o grupo</Link>
      )}
    </section>
  );
}
