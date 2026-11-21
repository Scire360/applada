"use client";

import { useEffect, useRef, useState } from "react";
import { appApi } from "@/lib/api-client";
import { matchDate, registrationLabel, type MatchDetails, type Page } from "@/lib/connected-matches";
import { deniedMessage, positionHeaders, readBrowserPosition, type BrowserPosition } from "@/lib/geo";
import { SignInAgain } from "../groups/sign-in-again";
import { useAction } from "../groups/use-action";

type OpenMatch = Pick<MatchDetails, "title" | "startsAt" | "timeZone"> & {
  matchId: string;
  venue: { id: string; name: string; addressText: string | null };
  distanceKm: number;
  spotsLeft: number;
};
type Registration = NonNullable<MatchDetails["myRegistration"]>;
const distance = new Intl.NumberFormat("pt-BR", { minimumFractionDigits: 1, maximumFractionDigits: 1 });

export function OpenMatches() {
  const [position, setPosition] = useState<BrowserPosition>();
  const [radius, setRadius] = useState(10);
  const [page, setPage] = useState<Page<OpenMatch>>();
  const [registrations, setRegistrations] = useState<Record<string, Registration>>({});
  const [progress, setProgress] = useState("");
  const { busy, error, notice, signedOut, run, setError, setNotice } = useAction();
  const focusRegistration = useRef<string | null>(null);
  const lifetime = useRef<AbortController>(null);
  useEffect(() => {
    const controller = new AbortController();
    lifetime.current = controller;
    return () => controller.abort();
  }, []);

  async function search(point: BrowserPosition, km: number, cursor?: string | null) {
    setProgress("Carregando partidas…");
    if (!cursor) setPage(undefined);
    const query = new URLSearchParams({ radiusKm: String(km) });
    if (cursor) query.set("cursor", cursor);
    const next = await appApi<Page<OpenMatch>>(`discover/matches?${query}`, {
      signal: lifetime.current?.signal,
      headers: { [positionHeaders.lat]: String(point.lat), [positionHeaders.lon]: String(point.lon) },
    });
    setPage((current) => cursor && current ? { items: [...current.items, ...next.items], nextCursor: next.nextCursor } : next);
  }

  function locate() {
    void run(async () => {
      setPosition(undefined);
      setPage(undefined);
      setProgress("Aguardando permissão para usar sua localização…");
      const point = await readBrowserPosition();
      if (lifetime.current?.signal.aborted) return;
      if (typeof point === "string") {
        setError(point === "denied" ? deniedMessage : "Localização indisponível. Confira se a localização do dispositivo está ativada e tente novamente.");
        return;
      }
      setPosition(point);
      await search(point, radius);
    });
  }

  function join({ matchId: id, title }: OpenMatch, button: HTMLButtonElement) {
    void run(async () => {
      setProgress("Enviando inscrição…");
      const path = `matches/${id}`;
      const registration = await appApi<Registration>(path + "/registrations", { method: "POST" });
      if (document.activeElement === button) focusRegistration.current = id;
      setRegistrations((current) => ({ ...current, [id]: registration }));
      setNotice(`${registrationLabel(registration.status, registration.position)} em ${title}.`);
      // A inscrição é idempotente. A contagem atual vem da API mesmo quando já havia inscrição.
      const match = await appApi<MatchDetails>(path, { signal: lifetime.current?.signal });
      setPage((current) => current && ({
        ...current,
        items: current.items.map((item) => item.matchId === id
          ? { ...item, spotsLeft: Math.max(0, match.capacity - match.confirmedCount) } : item),
      }));
    });
  }

  return (
    <section className="surface-card connected-card" aria-labelledby="buscar-peladas">
      <h2 id="buscar-peladas">Partidas perto de mim</h2>
      <p>A busca usa sua localização aproximada enquanto esta página está aberta.</p>
      <div className="connected-actions">
        <label className="preview-field">
          <span>Raio de busca</span>
          <select disabled={busy} value={radius} onChange={(event) => {
            const km = Number(event.target.value);
            setRadius(km);
            if (position) void run(() => search(position, km));
          }}>
            {[5, 10, 25, 50].map((km) => <option key={km} value={km}>{km} km</option>)}
          </select>
        </label>
        <button className="button button-primary" disabled={busy || signedOut} onClick={locate} type="button">Usar minha localização</button>
      </div>
      <p role="status">{notice || (busy ? progress : page?.items.length === 0 ? "Nenhuma pelada encontrada neste raio. Experimente aumentar a distância." : "")}</p>
      {error && <p className="form-feedback form-feedback-error" role="alert">{error}</p>}
      {signedOut && <SignInAgain />}
      {error && !signedOut && position && !page && (
        <button className="button button-secondary" disabled={busy} onClick={() => void run(() => search(position, radius))} type="button">Tentar a busca novamente</button>
      )}
      {page && page.items.length > 0 && (
        <ul aria-label="Partidas encontradas" className="connected-list">
          {page.items.map((match) => {
            const registration = registrations[match.matchId];
            return (
              <li className="connected-card" key={match.matchId}>
                <h3>{match.title}</h3>
                <p>{matchDate(match.startsAt, match.timeZone)}</p>
                <p><strong>{match.venue.name}</strong> · {distance.format(match.distanceKm)} km</p>
                {match.venue.addressText && <p>{match.venue.addressText}</p>}
                <p>{match.spotsLeft === 1 ? "1 vaga disponível" : `${match.spotsLeft} vagas disponíveis`}{match.spotsLeft === 0 && " · Lista de espera"}</p>
                {registration ? (
                  <p tabIndex={-1} ref={(status) => {
                    if (status && focusRegistration.current === match.matchId) {
                      focusRegistration.current = null;
                      status.focus();
                    }
                  }}>{registrationLabel(registration.status, registration.position)}</p>
                ) : (
                  <button className="button button-primary" disabled={signedOut} aria-disabled={busy || signedOut}
                    onClick={(event) => { if (!busy) join(match, event.currentTarget); }} type="button">Entrar</button>
                )}
              </li>
            );
          })}
        </ul>
      )}
      {position && page?.nextCursor && (
        <button className="button button-secondary" disabled={busy || signedOut} onClick={() => void run(() => search(position, radius, page.nextCursor))} type="button">Ver mais</button>
      )}
    </section>
  );
}
