"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { ApiError, appApi } from "@/lib/api-client";
import { guidPattern } from "@/lib/guid";
import {
  matchDate, registrationLabel,
  type GroupSummary, type MatchCreation, type MatchDetails, type MatchEdit, type MatchRole, type MatchSummary, type Page, type Participant,
} from "@/lib/connected-matches";
import { CreateConnectedMatch, EditConnectedMatch } from "./connected-match-forms";
import { ConnectedProfile } from "./connected-profile";
import { useAccount } from "./account-provider";
import { AttendanceLink } from "./matches/attendance-link";

type Session = { userId: string };
type Phase = "checking" | "signed-out" | "error" | "signed-in";
const blankPage = { items: [], nextCursor: null };
const roles: MatchRole[] = ["organizer", "participant"];
const roleLabel = { organizer: "Organizo", participant: "Participo" };
const connectedPath = "/peladas/conectadas";
const maxGroupPages = 10;

function mine(role: MatchRole, cursor?: string) {
  const query = new URLSearchParams({ role });
  if (cursor) query.set("cursor", cursor);
  return appApi<Page<MatchSummary>>(`me/matches?${query}`);
}

// Grupos em que posso criar partida (owner ou admin), para o seletor opcional.
async function managedGroups(signal: AbortSignal) {
  const groups: GroupSummary[] = [];
  let cursor: string | null = null;
  for (let page = 0; page < maxGroupPages; page++) {
    const query: string = cursor ? `?cursor=${encodeURIComponent(cursor)}` : "";
    const next: Page<GroupSummary> = await appApi<Page<GroupSummary>>(`me/groups${query}`, { signal });
    groups.push(...next.items.filter((group) => group.myRole === "owner" || group.myRole === "admin"));
    cursor = next.nextCursor;
    if (!cursor) break;
  }
  return groups;
}

function detailPath(id: string) {
  return `matches/${id}`;
}

export function ConnectedMatches() {
  const { account } = useAccount();
  const [phase, setPhase] = useState<Phase>("checking");
  const [session, setSession] = useState<Session>();
  const [role, setRole] = useState<MatchRole>("organizer");
  const [page, setPage] = useState<Page<MatchSummary>>(blankPage);
  const [match, setMatch] = useState<MatchDetails>();
  const [participants, setParticipants] = useState<Page<Participant>>();
  const [completedCreations, setCompletedCreations] = useState(0);
  const [groups, setGroups] = useState<GroupSummary[]>([]);
  const [initialGroupId, setInitialGroupId] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [now, setNow] = useState(0);
  const inFlight = useRef(false);
  const creationKey = useRef<{ body: string; key: string }>(null);

  useEffect(() => {
    const controller = new AbortController();
    const url = new URL(window.location.href);
    const rawId = url.searchParams.get("partida") ?? "";
    const id = guidPattern.test(rawId) ? rawId : "";
    const rawGroup = url.searchParams.get("grupo") ?? "";
    const groupId = guidPattern.test(rawGroup) ? rawGroup.toLowerCase() : "";

    async function load() {
      let authenticated = false;
      try {
        const identity = await appApi<Session>("me", { signal: controller.signal });
        if (controller.signal.aborted) return;
        authenticated = true;
        setSession(identity);
        const [list, selected, managed] = await Promise.allSettled([
          mine("organizer"),
          id ? appApi<MatchDetails>(detailPath(id), { signal: controller.signal }) : Promise.resolve(undefined),
          managedGroups(controller.signal),
        ]);
        if (controller.signal.aborted) return;
        if (list.status === "fulfilled") setPage(list.value);
        if (selected.status === "fulfilled") setMatch(selected.value);
        if (managed.status === "fulfilled") {
          setGroups(managed.value);
          const preselected = managed.value.find((group) => group.id.toLowerCase() === groupId);
          if (preselected) setInitialGroupId(preselected.id);
        }
        setNow(Date.now());
        setPhase("signed-in");
        const failure = [list, selected, managed].find((result) => result.status === "rejected");
        if (failure?.status === "rejected") throw failure.reason;
      } catch (failure) {
        if (controller.signal.aborted) return;
        if (failure instanceof ApiError && failure.status === 401) {
          setPhase("signed-out");
          setError(failure.message);
        } else {
          setPhase(authenticated ? "signed-in" : "error");
          setError(failure instanceof ApiError ? failure.message : "Não foi possível conectar agora.");
        }
      }
    }

    void load();
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (!match) return;
    const timer = window.setInterval(() => setNow(Date.now()), 15_000);
    return () => window.clearInterval(timer);
  }, [match]);

  useEffect(() => {
    if (completedCreations) document.getElementById("partida-selecionada")?.focus();
  }, [completedCreations]);

  async function run(action: () => Promise<void>) {
    if (inFlight.current) return;
    inFlight.current = true;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await action();
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 401) setPhase("signed-out");
      setError(failure instanceof ApiError ? failure.message : "Não foi possível concluir a ação.");
    } finally {
      setBusy(false);
      inFlight.current = false;
    }
  }

  function select(next: MatchDetails) {
    setNow(Date.now());
    setMatch(next);
    setParticipants(undefined);
    window.history.replaceState(null, "", `${connectedPath}?partida=${next.id}`);
  }

  async function reloadAfterWrite(reload: () => Promise<void>) {
    try {
      await reload();
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 401) throw failure;
      setError("A alteração foi salva. Atualize a página para carregar os dados mais recentes.");
    }
  }

  function create(fields: MatchCreation) {
    void run(async () => {
      const body = JSON.stringify({ ...fields, locationText: fields.locationText || null, groupId: fields.groupId || null });
      if (creationKey.current?.body !== body) creationKey.current = { body, key: crypto.randomUUID() };
      const next = await appApi<MatchDetails>("matches", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Idempotency-Key": creationKey.current.key },
        body,
      });
      creationKey.current = null;
      select(next);
      setCompletedCreations((count) => count + 1);
      setNotice("Partida criada.");
      await reloadAfterWrite(async () => {
        setPage(await mine("organizer"));
        setRole("organizer");
      });
    });
  }

  function refresh(id: string) {
    return run(async () => {
      select(await appApi<MatchDetails>(detailPath(id)));
      setPage(await mine(role));
      setNotice("Partida atualizada.");
    });
  }

  function changeRegistration(id: string, withdraw: boolean) {
    void run(async () => {
      const path = detailPath(id) + "/registrations";
      const outcome = await appApi<MatchDetails["myRegistration"]>(withdraw ? path + "/me" : path, { method: withdraw ? "DELETE" : "POST" });
      setNotice(withdraw ? "Inscrição retirada." : outcome?.status === "waitlisted" ? "Você entrou na lista de espera." : "Sua vaga está confirmada.");
      await reloadAfterWrite(async () => {
        select(await appApi<MatchDetails>(detailPath(id)));
        setPage(await mine(role));
      });
    });
  }

  function save(current: MatchDetails, fields: MatchEdit) {
    void run(async () => {
      const next = await appApi<MatchDetails>(detailPath(current.id), {
        method: "PATCH",
        headers: { "Content-Type": "application/json", "If-Match": `"${current.version}"` },
        body: JSON.stringify(fields),
      });
      select(next);
      setNotice("Alterações salvas.");
      await reloadAfterWrite(async () => setPage(await mine(role)));
    });
  }

  function cancel(id: string) {
    void run(async () => {
      select(await appApi<MatchDetails>(detailPath(id) + "/cancellation", { method: "POST" }));
      setNotice("Partida cancelada.");
      await reloadAfterWrite(async () => setPage(await mine(role)));
    });
  }

  function listParticipants(id: string, cursor?: string) {
    return run(async () => {
      const query = cursor ? `?cursor=${encodeURIComponent(cursor)}` : "";
      const next = await appApi<Page<Participant>>(detailPath(id) + "/registrations" + query);
      setParticipants((current) => cursor && current
        ? { items: [...current.items, ...next.items], nextCursor: next.nextCursor } : next);
    });
  }

  if (phase !== "signed-in") {
    return (
      <section className="surface-card connected-card" aria-labelledby="conexao">
        <h2 id="conexao">{phase === "signed-out" ? "Entre para continuar" : "Suas partidas"}</h2>
        {phase === "checking" && <p role="status">Verificando a sessão…</p>}
        {phase === "signed-out" && (
          <>
            <p>Entre na sua conta do Applada para organizar partidas e confirmar presença.</p>
            <Link className="button button-primary" href="/login">Entrar</Link>
          </>
        )}
        {phase === "error" && <button className="button button-primary" onClick={() => window.location.reload()} type="button">Tentar novamente</button>}
        {error && <p className="form-feedback form-feedback-error" role="alert">{error}</p>}
      </section>
    );
  }

  const organizer = match?.organizerId === session?.userId;
  const activeRegistration = match?.myRegistration?.status === "confirmed" || match?.myRegistration?.status === "waitlisted";
  const isOpen = match?.status === "scheduled" && new Date(match.startsAt).getTime() > now;

  return (
    <>
      <div className="connected-feedback">
        <p role="status">{busy ? "Aguarde…" : notice}</p>
        {error && <p className="form-feedback form-feedback-error" role="alert">{error}</p>}
      </div>

      <ConnectedProfile />

      {match && (
        <section className="surface-card connected-card" aria-labelledby="partida-selecionada" aria-busy={busy}>
          <div className="section-title-row">
            <div>
              <h2 id="partida-selecionada" tabIndex={-1}>{match.title}</h2>
              <p>{matchDate(match.startsAt, match.timeZone)} · {match.durationMinutes} min{match.locationText ? ` · ${match.locationText}` : ""}</p>
            </div>
            <span className="quiet-label">{match.status === "cancelled" ? "Cancelada" : isOpen ? "Agendada" : "Inscrições encerradas"}</span>
          </div>
          <p>{match.confirmedCount} de {match.capacity} vagas ocupadas · {match.waitlistCount} na lista de espera</p>
          {match.myRegistration && <p><strong>{registrationLabel(match.myRegistration.status, match.myRegistration.position)}</strong></p>}
          <div className="connected-actions">
            {organizer && session && <AttendanceLink match={match} userId={session.userId} rowTargetId="partida-selecionada" />}
            {isOpen && (
              <button className={`button button-${activeRegistration ? "secondary" : "primary"}`} disabled={busy} onClick={() => changeRegistration(match.id, activeRegistration)} type="button">
                {activeRegistration ? "Retirar inscrição" : match.confirmedCount >= match.capacity ? "Entrar na lista de espera" : "Confirmar presença"}
              </button>
            )}
            <button className="button button-secondary" disabled={busy} onClick={() => void refresh(match.id)} type="button">Atualizar partida</button>
            <button className="button button-secondary" disabled={busy} onClick={() => void run(async () => {
              await navigator.clipboard.writeText(new URL(`${connectedPath}?partida=${match.id}`, window.location.origin).href);
              setNotice("Link copiado.");
            })} type="button">Copiar link da partida</button>
            {(organizer || activeRegistration) && (
              <button className="button button-secondary" disabled={busy} onClick={() => void listParticipants(match.id)} type="button">Ver participantes</button>
            )}
          </div>
          {participants && (
            <div className="connected-participants">
              <h3>Participantes</h3>
              {participants.items.length ? <ul>{participants.items.map((item) => {
                const name = account.state === "ready" && account.userId === item.userId ? account.displayName : item.displayName;
                return <li key={item.userId}>
                  {name ?? <code>{item.userId}</code>} · {registrationLabel(item.status, item.position)}
                </li>;
              })}</ul> : <p>Nenhuma inscrição ativa.</p>}
              {participants.nextCursor && <button className="button button-secondary" disabled={busy} onClick={() => void listParticipants(match.id, participants.nextCursor ?? undefined)} type="button">Mais participantes</button>}
            </div>
          )}
          {organizer && isOpen && (
            <div className="connected-organizer">
              <details>
                <summary>Editar partida</summary>
                <EditConnectedMatch busy={busy} key={`${match.id}-${match.version}`} match={match} save={(fields) => save(match, fields)} />
              </details>
              <details>
                <summary>Cancelar partida</summary>
                <p>O cancelamento encerra as inscrições e fica visível para os participantes.</p>
                <button className="button connected-danger" disabled={busy} onClick={() => cancel(match.id)} type="button">Confirmar cancelamento</button>
              </details>
            </div>
          )}
        </section>
      )}

      <div className="connected-grid">
        <section className="surface-card connected-card" aria-labelledby="minhas-partidas">
          <h2 id="minhas-partidas">Minhas partidas</h2>
          <div aria-label="Filtrar minhas partidas" className="connected-actions" role="group">
            {roles.map((option) => (
              <button aria-pressed={role === option} className={`button button-${role === option ? "primary" : "secondary"}`} disabled={busy} key={option}
                onClick={() => void run(async () => { setPage(await mine(option)); setRole(option); })} type="button">{roleLabel[option]}</button>
            ))}
          </div>
          {page.items.length ? (
            <ul className="connected-list">
              {page.items.map((item) => (
                <li key={item.id} id={`minha-partida-${item.id}`} tabIndex={-1}>
                  <button className="connected-match-item" aria-pressed={match?.id === item.id} disabled={busy} onClick={() => void refresh(item.id)} type="button">
                    <strong>{item.title}</strong>
                    <span>{matchDate(item.startsAt)} · {item.status === "cancelled" ? "Cancelada" : `${item.confirmedCount}/${item.capacity} vagas`}</span>
                  </button>
                  {session && match?.id !== item.id && <AttendanceLink match={item} userId={session.userId} knownOrganizer={role === "organizer"} rowTargetId={`minha-partida-${item.id}`} />}
                </li>
              ))}
            </ul>
          ) : <p>{role === "organizer" ? "Você ainda não organizou partidas." : "Suas inscrições ativas aparecem aqui."}</p>}
          {page.nextCursor && <button className="button button-secondary" disabled={busy} onClick={() => void run(async () => {
            const next = await mine(role, page.nextCursor ?? undefined);
            setPage({ items: [...page.items, ...next.items], nextCursor: next.nextCursor });
          })} type="button">Mais partidas</button>}
        </section>
        <section className="surface-card connected-card" aria-labelledby="criar-partida">
          <h2 id="criar-partida">Organizar partida</h2>
          <CreateConnectedMatch busy={busy} create={create} groups={groups} initialGroupId={initialGroupId} key={`${completedCreations}-${initialGroupId}`} />
        </section>
      </div>
    </>
  );
}
