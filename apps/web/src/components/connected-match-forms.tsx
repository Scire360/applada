"use client";

import { useState, type FormEvent } from "react";
import type { MatchCreation, MatchDetails, MatchEdit } from "@/lib/connected-matches";

function readMatch(form: FormData): MatchEdit {
  return {
    title: String(form.get("titulo")).trim(),
    locationText: String(form.get("local")).trim(),
    capacity: Number(form.get("vagas")),
  };
}

function MatchFields({ match }: { match?: MatchDetails }) {
  return (
    <>
      <label className="preview-field preview-field-wide">
        <span>Nome da partida</span>
        <input defaultValue={match?.title} maxLength={100} name="titulo" placeholder="Ex.: Futebol de quinta" required />
      </label>
      <label className="preview-field preview-field-wide">
        <span>Local e campo <small>(opcional)</small></span>
        <input defaultValue={match?.locationText ?? ""} maxLength={200} name="local" placeholder="Ex.: Arena Santa Cecília, campo 2" />
      </label>
      <label className="preview-field">
        <span>Número de vagas</span>
        <input defaultValue={match?.capacity ?? 18} max={200} min={1} name="vagas" required type="number" />
      </label>
    </>
  );
}

export function CreateConnectedMatch({ busy, create }: { busy: boolean; create: (fields: MatchCreation) => void }) {
  const [error, setError] = useState("");

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const fields = readMatch(data);
    const rawStart = String(data.get("inicio"));
    const start = new Date(rawStart);
    // A nonexistent local time during a DST change is normalized by Date. Require an exact round trip.
    const localStart = new Date(start.getTime() - start.getTimezoneOffset() * 60_000);
    if (!fields.title || !Number.isFinite(start.getTime()) || start.getTime() <= Date.now()
        || localStart.toISOString().slice(0, 16) !== rawStart) {
      setError("Informe um nome e escolha uma data e um horário válidos no futuro.");
      return;
    }
    setError("");
    create({
      ...fields,
      startsAt: start.toISOString(),
      timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
      durationMinutes: Number(data.get("duracao")),
      visibility: "public",
    });
  }

  return (
    <form className="preview-form" onSubmit={submit}>
      <fieldset className="connected-fieldset" disabled={busy}>
        <div className="preview-form-grid">
          <MatchFields />
          <label className="preview-field">
            <span>Duração em minutos</span>
            <input defaultValue={90} max={600} min={15} name="duracao" required type="number" />
          </label>
          <label className="preview-field preview-field-wide">
            <span>Início no horário local do seu dispositivo</span>
            <input name="inicio" required type="datetime-local" />
          </label>
        </div>
        <p className="connected-hint">Qualquer conta de teste com o link poderá consultar a partida e se inscrever.</p>
        <button className="button button-primary" type="submit">Criar partida</button>
      </fieldset>
      {error && <p className="form-feedback form-feedback-error" role="alert">{error}</p>}
    </form>
  );
}

export function EditConnectedMatch({ match, busy, save }: {
  match: MatchDetails;
  busy: boolean;
  save: (fields: MatchEdit) => void;
}) {
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    save(readMatch(new FormData(event.currentTarget)));
  }

  return (
    <form className="preview-form" onSubmit={submit}>
      <fieldset className="connected-fieldset" disabled={busy}>
        <div className="preview-form-grid"><MatchFields match={match} /></div>
        <button className="button button-primary" type="submit">Salvar alterações</button>
      </fieldset>
    </form>
  );
}
