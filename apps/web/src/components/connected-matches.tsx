"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import {
  ApiError, backendApi, localApi, matchDate, registrationLabel,
  type ApiCall,
  type MatchCreation, type MatchDetails, type MatchEdit, type MatchRole, type MatchSummary, type Page, type Participant,
} from "@/lib/connected-matches";
import { matchIdPattern } from "@/lib/api-routes";
import { CreateConnectedMatch, EditConnectedMatch } from "./connected-match-forms";
import { ConnectedProfile } from "./connected-profile";
import { useAccount } from "./account-provider";

type Session = { userId: string };
type Phase = "checking" | "disabled" | "signed-out" | "error" | "signed-in";
const blankPage = { items: [], nextCursor: null };
const roles: MatchRole[] = ["organizer", "participant"];
const roleLabel = { organizer: "Organizo", participant: "Participo" };
const connectedPath = "/peladas/conectadas";
type LoginReturnPath = typeof connectedPath | "/peladas" | "/eventos/novo";

function mine(api: ApiCall, role: MatchRole, cursor?: string) {
  const query = new URLSearchParams({ role });
  if (cursor) query.set("cursor", cursor);
  return api<Page<MatchSummary>>(`mine?${query}`);
}

function detailPath(id: string) {
  return `matches/${id}`;
}

export function ConnectedMatches({
  apiMode = "local",
  loginReturnTo = connectedPath,
}: {
  apiMode?: "local" | "backend";
  loginReturnTo?: LoginReturnPath;
}) {
  const api: ApiCall = apiMode === "backend" ? backendApi : localApi;
  const { account } = useAccount();
  const [phase, setPhase] = useState<Phase>("checking");
  const [session, setSession] = useState<Session>();
  const [role, setRole] = useState<MatchRole>("organizer");
  const [page, setPage] = useState<Page<MatchSummary>>(blankPage);
  const [match, setMatch] = useState<MatchDetails>();
  const [requestedId, setRequestedId] = useState("");
  const [participants, setParticipants] = useState<Page<Participant>>();
  const [completedCreations, setCompletedCreations] = useState(0);
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
    const id = matchIdPattern.test(rawId) ? rawId : "";

    async function load() {
      let authenticated = false;
      try {
        const identity = await api<Session>("session", { signal: controller.signal });
        if (controller.signal.aborted) return;
        authenticated = true;
        setSession(identity);
        const [list, selected] = await Promise.allSettled([
          mine(api, "organizer"),
          id ? api<MatchDetails>(detailPath(id), { signal: controller.signal }) : Promise.resolve(undefined),
        ]);
        if (controller.signal.aborted) return;
        if (list.status === "fulfilled") setPage(list.value);
        if (selected.status === "fulfilled") setMatch(selected.value);
        setNow(Date.now());
        setPhase("signed-in");
        const failure = [list, selected].find((result) => result.status === "rejected");
        if (failure?.status === "rejected") throw failure.reason;
      } catch (failure) {
        if (controller.signal.aborted) return;
        const status = failure instanceof ApiError ? failure.status : 0;
        if (status === 401) {
          setPhase("signed-out");
          if (url.searchParams.has("login")) setError("O login não foi concluído. Tente novamente.");
        } else if (status === 404 && !authenticated) {
          setPhase("disabled");
        } else {
          setPhase(authenticated ? "signed-in" : "error");
          setError(failure instanceof Error ? failure.message : "Não foi possível conectar agora.");
        }
      } finally {
        if (!controller.signal.aborted) setRequestedId(id);
      }
    }

    void load();
    return () => controller.abort();
  }, [api]);

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
      setError(failure instanceof Error ? failure.message : "Não foi possível concluir a ação.");
    } finally {
      setBusy(false);
      inFlight.current = false;
    }
  }

  function select(next: MatchDetails) {
    setNow(Date.now());
    setMatch(next);
    setRequestedId(next.id);
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
      const body = JSON.stringify({ ...fields, locationText: fields.locationText || null });
      if (creationKey.current?.body !== body) creationKey.current = { body, key: crypto.randomUUID() };
      const next = await api<MatchDetails>("matches", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Idempotency-Key": creationKey.current.key },
        body,
      });
      creationKey.current = null;
      select(next);
      setCompletedCreations((count) => count + 1);
      setNotice("Partida criada.");
      await reloadAfterWrite(async () => {
        setPage(await mine(api, "organizer"));
        setRole("organizer");
      });
    });
  }

  function refresh(id: string) {
    return run(async () => {
      select(await api<MatchDetails>(detailPath(id)));
      setPage(await mine(api, role));
      setNotice("Partida atualizada.");
    });
  }

  function changeRegistration(id: string, withdraw: boolean) {
    void run(async () => {
      const path = detailPath(id) + "/registrations";
      const outcome = await api<MatchDetails["myRegistration"]>(withdraw ? path + "/me" : path, { method: withdraw ? "DELETE" : "POST" });
      setNotice(withdraw ? "Inscrição retirada." : outcome?.status === "waitlisted" ? "Você entrou na lista de espera." : "Sua vaga está confirmada.");
      await reloadAfterWrite(async () => {
        select(await api<MatchDetails>(detailPath(id)));
        setPage(await mine(api, role));
      });
    });
  }

  function save(current: MatchDetails, fields: MatchEdit) {
    void run(async () => {
      const next = await api<MatchDetails>(detailPath(current.id), {
        method: "PATCH",
        headers: { "Content-Type": "application/json", "If-Match": `"${current.version}"` },
        body: JSON.stringify(fields),
      });
      select(next);
      setNotice("Alterações salvas.");
      await reloadAfterWrite(async () => setPage(await mine(api, role)));
    });
  }

  function cancel(id: string) {
    void run(async () => {
      select(await api<MatchDetails>(detailPath(id) + "/cancellation", { method: "POST" }));
      setNotice("Partida cancelada.");
      await reloadAfterWrite(async () => setPage(await mine(api, role)));
    });
  }

  function listParticipants(id: string, cursor?: string) {
    return run(async () => {
      const query = cursor ? `?cursor=${encodeURIComponent(cursor)}` : "";
      const next = await api<Page<Participant>>(detailPath(id) + "/registrations" + query);
      setParticipants((current) => cursor && current
        ? { items: [...current.items, ...next.items], nextCursor: next.nextCursor } : next);
    });
  }

  if (phase !== "signed-in") {
    const loginQuery = new URLSearchParams({ returnTo: loginReturnTo });
    if (requestedId) loginQuery.set("partida", requestedId);
    return (
      <section className="surface-card connected-card" aria-labelledby="conexao">
        <h2 id="conexao">{phase === "disabled" ? "Integração local desativada" : "Conta de teste"}</h2>
        {phase === "checking" && <p role="status">Verificando a sessão…</p>}
        {phase === "disabled" && <p>Para abrir as partidas reais do ambiente de desenvolvimento, inicie a web com a integração local habilitada.</p>}
        {phase === "signed-out" && apiMode === "local" && (
          <form action={`/api/local/login?${loginQuery}`} method="post">
            <p>Entre pelo Keycloak local para organizar partidas e ocupar vagas no banco de teste.</p>
            <button className="button button-primary" type="submit">Entrar com conta de teste</button>
          </form>
        )}
        {phase === "signed-out" && apiMode === "backend" && (
          <div>
            <p>Sua sessão expirou. Entre novamente para continuar.</p>
            <Link className="button button-primary" href="/login">Entrar com minha conta</Link>
          </div>
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
      {apiMode === "local" && (
        <div className="connected-session">
          <span className="quiet-label"><span className="status-dot" /> Sessão de teste ativa</span>
          <form action="/api/local/logout" method="post">
            <button className="button button-secondary" type="submit">Sair da sessão</button>
          </form>
        </div>
      )}
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
                onClick={() => void run(async () => { setPage(await mine(api, option)); setRole(option); })} type="button">{roleLabel[option]}</button>
            ))}
          </div>
          {page.items.length ? (
            <ul className="connected-list">
              {page.items.map((item) => (
                <li key={item.id}>
                  <button className="connected-match-item" aria-pressed={match?.id === item.id} disabled={busy} onClick={() => void refresh(item.id)} type="button">
                    <strong>{item.title}</strong>
                    <span>{matchDate(item.startsAt)} · {item.status === "cancelled" ? "Cancelada" : `${item.confirmedCount}/${item.capacity} vagas`}</span>
                  </button>
                </li>
              ))}
            </ul>
          ) : <p>{role === "organizer" ? "Você ainda não organizou partidas." : "Suas inscrições ativas aparecem aqui."}</p>}
          {page.nextCursor && <button className="button button-secondary" disabled={busy} onClick={() => void run(async () => {
            const next = await mine(api, role, page.nextCursor ?? undefined);
            setPage({ items: [...page.items, ...next.items], nextCursor: next.nextCursor });
          })} type="button">Mais partidas</button>}
        </section>
        <section className="surface-card connected-card" aria-labelledby="criar-partida">
          <h2 id="criar-partida">Organizar partida</h2>
          <CreateConnectedMatch busy={busy} create={create} key={completedCreations} />
        </section>
      </div>
    </>
  );
}
