"use client";

import { useEffect, useLayoutEffect, useRef, useState, type FormEvent } from "react";
import { ApiError, appApi } from "@/lib/api-client";
import type { Page } from "@/lib/connected-matches";
import { deniedMessage, readBrowserPosition } from "@/lib/geo";
import { SignInAgain } from "../groups/sign-in-again";
import { useAction } from "../groups/use-action";

type Venue = { id: string; name: string };
type Props = {
  value: string;
  onChange: (id: string) => void;
  editing: boolean;
  onEdit: (editing: boolean) => void;
  disabled: boolean;
};

export function VenuePicker({ value, onChange, editing, onEdit, disabled }: Props) {
  const [page, setPage] = useState<Page<Venue>>();
  const [latitude, setLatitude] = useState("");
  const [longitude, setLongitude] = useState("");
  const { busy, error, notice, signedOut, run, setError, setNotice, setSignedOut } = useAction();
  const nameInput = useRef<HTMLInputElement>(null);
  const venueSelect = useRef<HTMLSelectElement>(null);
  const transferFocus = useRef(false);
  const locked = disabled || busy || signedOut;

  useLayoutEffect(() => {
    if (!transferFocus.current || locked) return;
    transferFocus.current = false;
    (editing ? nameInput.current : venueSelect.current)?.focus();
  }, [editing, locked]);

  useEffect(() => {
    const controller = new AbortController();
    appApi<Page<Venue>>("me/venues", { signal: controller.signal })
      .then(setPage)
      .catch((failure: unknown) => {
        if (controller.signal.aborted) return;
        if (failure instanceof ApiError && failure.status === 401) setSignedOut(true);
        setError(failure instanceof ApiError ? failure.message : "Não foi possível carregar seus locais.");
      });
    return () => controller.abort();
  }, [setError, setSignedOut]);

  function loadVenues() {
    void run(async () => {
      const cursor = page?.nextCursor;
      const next = await appApi<Page<Venue>>(cursor ? `me/venues?cursor=${encodeURIComponent(cursor)}` : "me/venues");
      setPage((current) => cursor && current ? { items: [...current.items, ...next.items], nextCursor: next.nextCursor } : next);
    });
  }

  function locate() {
    void run(async () => {
      setNotice("Aguardando a localização do dispositivo…");
      const point = await readBrowserPosition();
      setNotice("");
      if (typeof point === "string") {
        setError(point === "denied" ? deniedMessage : "Localização indisponível. Você pode preencher as coordenadas do local.");
        return;
      }
      setLatitude(String(point.lat));
      setLongitude(String(point.lon));
      setNotice("Coordenadas preenchidas. Confira a posição do local antes de salvar.");
    });
  }

  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (locked) return;
    const { submitter } = event.nativeEvent as SubmitEvent;
    const data = new FormData(event.currentTarget);
    const name = String(data.get("nome")).trim();
    if (name.length < 3) {
      setError("O nome do local precisa ter pelo menos 3 caracteres.");
      return;
    }
    void run(async () => {
      const venue = await appApi<Venue>("venues", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name, addressText: String(data.get("endereco")).trim() || null, latitude: Number(latitude), longitude: Number(longitude) }),
      });
      setPage((current) => ({ items: [venue, ...(current?.items ?? [])], nextCursor: current?.nextCursor ?? null }));
      onChange(venue.id);
      transferFocus.current = submitter !== null && document.activeElement === submitter;
      onEdit(false);
      setNotice("Local cadastrado e selecionado para a partida.");
    });
  }

  function edit(open: boolean, button: HTMLButtonElement) {
    setError("");
    setNotice("");
    setLatitude("");
    setLongitude("");
    transferFocus.current = document.activeElement === button;
    onEdit(open);
  }

  return (
    <div className="preview-form">
      <label className="preview-field">
        <span>Meus locais <small>(opcional)</small></span>
        <select ref={venueSelect} disabled={locked || editing || !page} onChange={(event) => onChange(event.target.value)} value={value}>
          <option value="">Sem local cadastrado</option>
          {page?.items.map((venue) => <option key={venue.id} value={venue.id}>{venue.name}</option>)}
        </select>
      </label>
      {!editing && (
        <div className="connected-actions">
          {(page?.nextCursor || (!page && error && !signedOut)) && (
            <button className="button button-secondary" disabled={locked} onClick={loadVenues} type="button">
              {page ? "Ver mais locais" : "Carregar locais novamente"}
            </button>
          )}
          <button className="button button-secondary" disabled={locked || !page} onClick={(event) => edit(true, event.currentTarget)} type="button">Cadastrar local</button>
        </div>
      )}
      {editing && (
        <form className="connected-fieldset" onSubmit={save} autoComplete="off">
          <fieldset className="connected-fieldset" disabled={locked}>
            <legend>Cadastrar local</legend>
            <p>Cadastre só locais públicos, como quadras e campos. Nunca use endereço de casa.</p>
            <div className="preview-form-grid">
              <label className="preview-field preview-field-wide">
                <span>Nome do local</span>
                <input ref={nameInput} name="nome" minLength={3} maxLength={80} required />
              </label>
              <label className="preview-field preview-field-wide">
                <span>Endereço <small>(opcional)</small></span>
                <input name="endereco" maxLength={200} />
              </label>
              <button className="button button-secondary preview-field-wide" onClick={locate} type="button">Usar minha localização atual</button>
              <label className="preview-field">
                <span>Latitude</span>
                <input name="latitude" type="number" step="any" min={-90} max={90} required value={latitude} onChange={(e) => setLatitude(e.target.value)} />
              </label>
              <label className="preview-field">
                <span>Longitude</span>
                <input name="longitude" type="number" step="any" min={-180} max={180} required value={longitude} onChange={(e) => setLongitude(e.target.value)} />
              </label>
            </div>
          </fieldset>
          <div className="connected-actions">
            <button className="button button-primary" type="submit" disabled={disabled || signedOut} aria-disabled={locked}>Salvar local</button>
            <button className="button button-secondary" type="button" disabled={locked} onClick={(event) => edit(false, event.currentTarget)}>Cancelar cadastro</button>
          </div>
        </form>
      )}
      <p role="status">{!page && !error ? "Carregando locais…" : notice || (busy ? "Aguarde…" : "")}</p>
      {error && <p className="form-feedback form-feedback-error" role="alert">{error}</p>}
      {signedOut && <SignInAgain />}
    </div>
  );
}
