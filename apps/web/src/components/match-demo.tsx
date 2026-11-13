"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { Icon } from "@/components/icon";
import { demoPlayers, useMatchDemo } from "@/components/match-demo-provider";
import type { MatchDraft } from "@/components/match-demo-provider";

type MatchDemoPanelProps = {
  mode: "open" | "presence" | "checkin";
};

const dateFormatter = new Intl.DateTimeFormat("pt-BR", {
  dateStyle: "long",
  timeZone: "UTC",
});

export function MatchDemoMetrics({ mode }: { mode: "attendance" | "game" }) {
  const { match, status, attendees, waitlist, checkedIn, goals, playerOfTheMatch } = useMatchDemo();
  const score = `${goals.filter((goal) => goal.side === "green").length} × ${goals.filter((goal) => goal.side === "blue").length}`;

  const metrics = mode === "game"
    ? [
      [match ? score : "—", "placar da prévia", "Central da partida"],
      [status === "live" ? "Ao vivo" : status === "completed" ? "Encerrada" : String(checkedIn.length), status === "scheduled" ? "com check-in" : "estado da partida", "Elenco desta sessão"],
    ]
    : [
      [`${attendees.length} / ${match?.capacity ?? "—"}`, "vagas confirmadas", "Pelada desta sessão"],
      [String(waitlist.length), "na lista de espera", "Promoção demonstrativa"],
    ];

  return (
    <section aria-label="Resumo da demonstração da partida" className="screen-metric-grid">
      <article className="screen-metric-card">
        <span className="stat-icon stat-icon-green"><Icon name="sparkles" /></span>
        <span className="eyebrow">{metrics[0][0]}</span>
        <strong>{metrics[0][1]}</strong>
      </article>
      <article className="screen-metric-card">
        <span className="stat-icon stat-icon-blue"><Icon name="calendar" /></span>
        <span className="eyebrow">{metrics[1][0]}</span>
        <strong>{metrics[1][1]}</strong>
      </article>
      <article className="screen-metric-card screen-metric-note">
        <span className="eyebrow">DESTAQUE</span>
        <strong>{playerOfTheMatch ?? (match ? metrics[1][2] : "Crie uma pelada para começar.")}</strong>
        <span>Prévia local; nada é salvo nem enviado.</span>
      </article>
    </section>
  );
}

export function MatchCreationForm() {
  const { createMatch } = useMatchDemo();
  const router = useRouter();
  const [error, setError] = useState("");

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const formData = new FormData(event.currentTarget);
    const draft: MatchDraft = {
      title: String(formData.get("match-title")).trim(),
      date: String(formData.get("match-date")),
      time: String(formData.get("match-time")),
      location: String(formData.get("match-location")).trim(),
      capacity: Number(formData.get("match-capacity")),
      notes: String(formData.get("match-notes")).trim(),
    };

    if (!draft.title || !draft.date || !draft.time || !draft.location || draft.capacity < 1) {
      setError("Preencha os campos obrigatórios e informe pelo menos uma vaga.");
      return;
    }

    setError("");
    createMatch(draft);
    router.push("/peladas");
  }

  return (
    <form className="preview-form" onSubmit={handleSubmit}>
      <div className="preview-form-grid">
        <label className="preview-field preview-field-wide" htmlFor="match-title">
          <span>Nome do encontro<span aria-hidden="true"> *</span></span>
          <input autoComplete="off" id="match-title" name="match-title" placeholder="Ex.: Quinta de futebol" required />
        </label>
        <label className="preview-field" htmlFor="match-date">
          <span>Data<span aria-hidden="true"> *</span></span>
          <input id="match-date" name="match-date" required type="date" />
        </label>
        <label className="preview-field" htmlFor="match-time">
          <span>Horário<span aria-hidden="true"> *</span></span>
          <input id="match-time" name="match-time" required type="time" />
        </label>
        <label className="preview-field preview-field-wide" htmlFor="match-location">
          <span>Local e campo<span aria-hidden="true"> *</span></span>
          <input autoComplete="off" id="match-location" name="match-location" placeholder="Arena, endereço ou ponto de encontro" required />
        </label>
        <label className="preview-field" htmlFor="match-capacity">
          <span>Número de vagas<span aria-hidden="true"> *</span></span>
          <input id="match-capacity" min="1" name="match-capacity" placeholder="18" required type="number" />
        </label>
        <label className="preview-field preview-field-wide" htmlFor="match-notes">
          <span>Detalhes para o time</span>
          <textarea id="match-notes" name="match-notes" placeholder="O que os jogadores precisam saber?" rows={3} />
        </label>
      </div>
      <div className="preview-form-actions">
        <button className="button button-primary" type="submit">Criar prévia da pelada</button>
        <span className="form-disclaimer">Demonstração local: os dados não serão enviados nem salvos.</span>
      </div>
      {error ? <p className="form-feedback form-feedback-error" role="alert">{error}</p> : null}
    </form>
  );
}

export function CreatedMatchScheduleRow() {
  const { match, attendees, waitlist } = useMatchDemo();
  if (!match) return null;

  const date = new Date(`${match.date}T12:00:00Z`);
  const weekday = new Intl.DateTimeFormat("pt-BR", { weekday: "short", timeZone: "UTC" })
    .format(date)
    .replace(".", "")
    .toLocaleUpperCase("pt-BR");
  const day = new Intl.DateTimeFormat("pt-BR", { day: "2-digit", timeZone: "UTC" }).format(date);
  const isFull = attendees.length >= match.capacity;

  return (
    <article className="schedule-row schedule-row-demo">
      <span className="date-tile"><span>{weekday}</span><strong>{day}</strong></span>
      <span className="schedule-info">
        <strong>{match.title}</strong>
        <span>{dateFormatter.format(date)} · {match.time} · {match.location}</span>
      </span>
      <span className="schedule-attendance">{attendees.length} / {match.capacity}<span>confirmados</span></span>
      <span className={`mini-pill ${isFull ? "pill-neutral" : "pill-green"}`}>
        {isFull ? waitlist.length ? `${waitlist.length} na espera` : "Lista completa" : "Prévia local"}
      </span>
      <Link className="demo-match-link" href="/peladas/abertas">Ver encontro</Link>
    </article>
  );
}

export function MatchDemoPanel({ mode }: MatchDemoPanelProps) {
  const { match, status, attendees, waitlist, checkedIn, toggleAttendance, toggleCheckIn } = useMatchDemo();
  const [notice, setNotice] = useState("");

  if (!match) {
    return (
      <div className="match-demo-empty">
        <p>Nenhuma pelada foi criada nesta sessão de demonstração.</p>
        <Link className="button button-primary" href="/eventos/novo">Organizar uma pelada</Link>
      </div>
    );
  }

  const isCheckIn = mode === "checkin";
  const candidates = isCheckIn
    ? demoPlayers.filter((player) => attendees.includes(player))
    : mode === "open" ? [demoPlayers[0]] : demoPlayers;
  const isFull = attendees.length >= match.capacity;
  const rosterIsLocked = status !== "scheduled";

  function handleAttendance(name: string) {
    const result = toggleAttendance(name);
    const nextInLine = waitlist[0];
    const messages = {
      confirmed: `${name} confirmado nesta prévia.`,
      cancelled: `${name} removido da lista de presença desta prévia.`,
      promoted: `${name} saiu da lista e ${nextInLine} assumiu a vaga.`,
      waitlisted: `${name} entrou na lista de espera, na posição ${waitlist.length + 1}.`,
      "left-waitlist": `${name} saiu da lista de espera.`,
      "match-started": "As confirmações estão bloqueadas porque a partida já começou.",
      unavailable: "Crie uma pelada antes de gerenciar a presença.",
    };
    setNotice(messages[result]);
  }

  function handleCheckIn(name: string) {
    const wasCheckedIn = checkedIn.includes(name);
    toggleCheckIn(name);
    setNotice(wasCheckedIn
      ? `Check-in de ${name} removido desta prévia.`
      : `Check-in de ${name} registrado nesta prévia.`);
  }

  return (
    <div className="match-demo-panel">
      {rosterIsLocked ? (
        <p className="match-status-banner match-status-locked" role="status">
          Confirmações e check-ins foram bloqueados após o início da partida.
        </p>
      ) : null}
      <section aria-label="Encontro da demonstração" className="match-demo-summary">
        <div>
          <p className="eyebrow">PELADA DESTA SESSÃO</p>
          <h3>{match.title}</h3>
          <p>{dateFormatter.format(new Date(`${match.date}T12:00:00Z`))} · {match.time}</p>
          <p>{match.location}</p>
          {match.notes ? <p>{match.notes}</p> : null}
        </div>
        <div aria-label={`${attendees.length} de ${match.capacity} vagas confirmadas`}
          aria-valuemax={match.capacity}
          aria-valuemin={0}
          aria-valuenow={attendees.length}
          className="demo-capacity"
          role="progressbar"
        >
          <strong>{attendees.length} / {match.capacity}</strong>
          <span>{waitlist.length ? `${waitlist.length} na lista de espera` : "vagas confirmadas"}</span>
        </div>
      </section>

      <div className="match-demo-roster">
        {mode === "open" ? (
          <div className="match-demo-action">
            <div>
              <strong>Rafael Martins</strong>
              <span>{attendees.includes(demoPlayers[0]) ? "Presença confirmada" : "Organizador do grupo"}</span>
            </div>
            <button
              aria-pressed={attendees.includes(demoPlayers[0])}
              className={`button ${attendees.includes(demoPlayers[0]) ? "button-secondary" : "button-primary"}`}
              disabled={rosterIsLocked || (!attendees.includes(demoPlayers[0]) && isFull)}
              onClick={() => handleAttendance(demoPlayers[0])}
              type="button"
            >
              {attendees.includes(demoPlayers[0]) ? "Cancelar presença" : "Confirmar presença"}
            </button>
          </div>
        ) : candidates.length ? candidates.map((player) => {
          const confirmed = attendees.includes(player);
          const waiting = waitlist.includes(player);
          const arrived = checkedIn.includes(player);

          return (
            <div className="match-demo-action" key={player}>
              <div>
                <strong>{player}</strong>
                <span>{isCheckIn
                  ? arrived ? "Chegou à partida" : "Presença confirmada"
                  : waiting
                    ? `Na lista de espera · posição ${waitlist.indexOf(player) + 1}`
                    : confirmed ? "Presença confirmada" : "Ainda não confirmou"}
                </span>
              </div>
              {isCheckIn ? (
                <button
                  aria-pressed={arrived}
                  className={`button ${arrived ? "button-secondary" : "button-primary"}`}
                  disabled={rosterIsLocked}
                  onClick={() => handleCheckIn(player)}
                  type="button"
                >
                  {arrived ? "Desfazer check-in" : "Registrar check-in"}
                </button>
              ) : (
                <button
                  aria-pressed={confirmed || waiting}
                  className={`button ${confirmed || waiting ? "button-secondary" : "button-primary"}`}
                  disabled={rosterIsLocked}
                  onClick={() => handleAttendance(player)}
                  type="button"
                >
                  {confirmed ? "Remover presença" : waiting ? "Sair da espera" : isFull ? "Entrar na espera" : "Confirmar presença"}
                </button>
              )}
            </div>
          );
        }) : (
          <p className="directory-empty">Confirme a presença de jogadores antes de fazer o check-in.</p>
        )}
      </div>

      {notice ? <p aria-live="polite" className="form-feedback" role="status">{notice}</p> : null}
      <div className="match-demo-links">
        {mode !== "presence" ? <Link href="/presenca">Gerenciar confirmações</Link> : null}
        {mode !== "checkin" ? <Link href="/presenca/check-in">Ir para check-in</Link> : null}
        <span>Estado somente nesta sessão; recarregar a página reinicia a prévia.</span>
      </div>
    </div>
  );
}
