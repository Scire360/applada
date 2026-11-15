"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { Icon } from "@/components/icon";
import { demoPlayers, useMatchDemo } from "@/components/match-demo-provider";
import type { MatchDraft } from "@/components/match-demo-provider";

type MatchDemoPanelProps = {
  mode: "open" | "presence" | "checkin";
};

export type ScheduledMatch = {
  date: string;
  title: string;
  time: string;
  place: string;
  confirmed: string;
  state: string;
  status: "open" | "planned";
};

const dateFormatter = new Intl.DateTimeFormat("pt-BR", {
  dateStyle: "long",
  timeZone: "UTC",
});

const weekdayFormatter = new Intl.DateTimeFormat("pt-BR", {
  weekday: "short",
  timeZone: "UTC",
});

const dayFormatter = new Intl.DateTimeFormat("pt-BR", {
  day: "2-digit",
  timeZone: "UTC",
});

function formatLocalDate(date: Date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

export function MatchDemoMetrics({ mode }: { mode: "attendance" | "checkin" | "game" }) {
  const { match, status, attendees, waitlist, checkedIn, goals, playerOfTheMatch } = useMatchDemo();
  const score = `${goals.filter((goal) => goal.side === "green").length} × ${goals.filter((goal) => goal.side === "blue").length}`;
  const pendingCheckIns = Math.max(0, attendees.length - checkedIn.length);

  const metrics = mode === "game"
    ? [
      [match ? score : "—", "placar da prévia", "Central da partida"],
      [status === "live" ? "Ao vivo" : status === "completed" ? "Encerrada" : String(checkedIn.length), status === "scheduled" ? "com check-in" : "estado da partida", "Elenco desta sessão"],
    ]
    : mode === "checkin"
      ? [
        [
          attendees.length ? `${checkedIn.length} / ${attendees.length}` : "—",
          "check-ins feitos",
          "Progresso da chegada",
        ],
        [
          attendees.length ? String(pendingCheckIns) : "—",
          attendees.length
            ? pendingCheckIns === 1 ? "check-in pendente" : "check-ins pendentes"
            : "jogadores confirmados",
          attendees.length ? "Pessoas confirmadas" : "Aguardando confirmações",
        ],
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
        <span className="eyebrow">{mode === "checkin" ? "ETAPA" : "DESTAQUE"}</span>
        <strong>{mode === "checkin"
          ? !match
            ? "Crie uma pelada para começar."
            : attendees.length
              ? pendingCheckIns ? "Check-in em andamento" : "Todos chegaram"
              : "Aguardando confirmações"
          : playerOfTheMatch ?? (match ? metrics[1][2] : "Crie uma pelada para começar.")}</strong>
        <span>Prévia local; nada é salvo nem enviado.</span>
      </article>
    </section>
  );
}

export function MatchCreationForm() {
  const { createMatch } = useMatchDemo();
  const router = useRouter();
  const [error, setError] = useState("");
  const dateInputRef = useRef<HTMLInputElement>(null);
  const timeInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (dateInputRef.current) dateInputRef.current.min = formatLocalDate(new Date());
  }, []);

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

    if (!draft.title || !draft.date || !draft.time || !draft.location) {
      setError("Preencha os campos obrigatórios e informe pelo menos uma vaga.");
      return;
    }
    if (!Number.isSafeInteger(draft.capacity) || draft.capacity < 1) {
      setError("Informe uma quantidade inteira de vagas igual ou maior que 1.");
      return;
    }

    const now = new Date();
    const today = formatLocalDate(now);
    if (draft.date < today) {
      setError("Escolha uma data de hoje ou posterior.");
      return;
    }
    if (draft.date === today && new Date(`${draft.date}T${draft.time}:00`) <= now) {
      setError("Escolha um horário futuro para a partida de hoje.");
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
          <input
            id="match-date"
            name="match-date"
            onChange={(event) => {
              const now = new Date();
              if (timeInputRef.current) {
                if (event.currentTarget.value === formatLocalDate(now)) {
                  now.setSeconds(0, 0);
                  now.setMinutes(now.getMinutes() + 1);
                  timeInputRef.current.min = `${String(now.getHours()).padStart(2, "0")}:${String(now.getMinutes()).padStart(2, "0")}`;
                } else {
                  timeInputRef.current.min = "";
                }
              }
            }}
            ref={dateInputRef}
            required
            type="date"
          />
        </label>
        <label className="preview-field" htmlFor="match-time">
          <span>Horário<span aria-hidden="true"> *</span></span>
          <input id="match-time" name="match-time" ref={timeInputRef} required type="time" />
        </label>
        <label className="preview-field preview-field-wide" htmlFor="match-location">
          <span>Local e campo<span aria-hidden="true"> *</span></span>
          <input autoComplete="off" id="match-location" name="match-location" placeholder="Arena, endereço ou ponto de encontro" required />
        </label>
        <label className="preview-field" htmlFor="match-capacity">
          <span>Número de vagas<span aria-hidden="true"> *</span></span>
          <input id="match-capacity" min="1" name="match-capacity" placeholder="18" required step="1" type="number" />
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

export function MatchSchedule({ matches }: { matches: ScheduledMatch[] }) {
  const { match, attendees, waitlist } = useMatchDemo();
  const items = [
    ...matches.map((item) => ({ ...item, isDemo: false })),
    ...(match ? [{
      date: match.date,
      title: match.title,
      time: match.time,
      place: match.location,
      confirmed: `${attendees.length} / ${match.capacity}`,
      state: attendees.length >= match.capacity
        ? waitlist.length ? `${waitlist.length} na espera` : "Lista completa"
        : "Prévia local",
      status: attendees.length >= match.capacity ? "planned" as const : "open" as const,
      isDemo: true,
    }] : []),
  ].sort((first, second) => first.date.localeCompare(second.date));

  return (
    <>
      {items.map((item) => {
        const date = new Date(`${item.date}T12:00:00Z`);
        const weekday = weekdayFormatter.format(date).replace(".", "").toLocaleUpperCase("pt-BR");
        const day = dayFormatter.format(date);

        return (
          <article className={`schedule-row${item.isDemo ? " schedule-row-demo" : ""}`} key={`${item.date}-${item.title}-${item.isDemo}`}>
            <time aria-label={dateFormatter.format(date)} className="date-tile" dateTime={item.date}>
              <span>{weekday}</span>
              <strong>{day}</strong>
            </time>
            <span className="schedule-info">
              <strong>{item.title}</strong>
              <span>{item.isDemo ? dateFormatter.format(date) : item.time} · {item.isDemo ? item.time : item.place}{item.isDemo ? ` · ${item.place}` : ""}</span>
            </span>
            <span className="schedule-attendance">{item.confirmed}<span>{item.isDemo ? "confirmados" : "jogadores"}</span></span>
            {item.isDemo ? (
              <>
                <span className={`mini-pill ${item.status === "open" ? "pill-green" : "pill-neutral"}`}>{item.state}</span>
                <Link className="demo-match-link" href="/peladas/abertas">Ver encontro</Link>
              </>
            ) : (
              <span className={`mini-pill ${item.status === "open" ? "pill-green" : "pill-neutral"}`}>{item.state}</span>
            )}
          </article>
        );
      })}
    </>
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
  const remainingSlots = Math.max(0, match.capacity - attendees.length);
  const progressValue = isCheckIn ? checkedIn.length : attendees.length;
  const progressMax = isCheckIn ? attendees.length : match.capacity;
  const progressPercent = progressMax
    ? Math.min(100, (progressValue / progressMax) * 100)
    : 0;
  const progressDescription = isCheckIn
    ? attendees.length
      ? `${checkedIn.length} de ${attendees.length} jogadores confirmados fizeram check-in`
      : "Nenhum jogador confirmou presença ainda"
    : `${attendees.length} confirmadas, ${remainingSlots} disponíveis e ${waitlist.length} na lista de espera`;
  const rosterIsLocked = status !== "scheduled";

  function handleAttendance(name: string) {
    const result = toggleAttendance(name);
    const nextInLine = waitlist[0];
    const messages = {
      confirmed: `${name} confirmado nesta prévia.`,
      cancelled: `${name} removido da lista de presença desta prévia.`,
      promoted: `${name} cancelou a presença e ${nextInLine} assumiu a vaga.`,
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
        <div
          aria-label={isCheckIn ? "Progresso dos check-ins" : `${attendees.length} de ${match.capacity} vagas confirmadas`}
          aria-valuemax={progressMax || undefined}
          aria-valuemin={0}
          aria-valuenow={progressMax ? progressValue : undefined}
          aria-valuetext={progressDescription}
          className="demo-capacity"
          role={progressMax ? "progressbar" : "status"}
        >
          <strong>{isCheckIn ? `${checkedIn.length} / ${attendees.length}` : `${attendees.length} / ${match.capacity}`}</strong>
          {progressMax ? (
            <div aria-hidden="true" className="demo-capacity-track">
              <span style={{ width: `${progressPercent}%` }} />
            </div>
          ) : null}
          <span>
            {isCheckIn
              ? attendees.length
                ? `${attendees.length - checkedIn.length} ${attendees.length - checkedIn.length === 1 ? "check-in pendente" : "check-ins pendentes"}`
                : "Aguardando confirmações"
              : isFull
              ? `Vagas esgotadas · ${waitlist.length ? `${waitlist.length} ${waitlist.length === 1 ? "pessoa" : "pessoas"} na espera` : "fila vazia"}`
              : `${remainingSlots} ${remainingSlots === 1 ? "vaga disponível" : "vagas disponíveis"}`}
          </span>
        </div>
      </section>

      <div className="match-demo-roster">
        {mode === "open" ? (
          <div className="match-demo-action">
            <div>
              <strong>Rafael Martins</strong>
              <span>
                {attendees.includes(demoPlayers[0])
                  ? "Presença confirmada"
                  : waitlist.includes(demoPlayers[0])
                    ? `Na lista de espera · posição ${waitlist.indexOf(demoPlayers[0]) + 1}`
                    : isFull
                      ? "Vagas esgotadas · entre na lista de espera"
                    : "Organizador do grupo"}
              </span>
            </div>
            <button
              aria-label={`${attendees.includes(demoPlayers[0])
                ? "Cancelar presença"
                : waitlist.includes(demoPlayers[0])
                  ? "Sair da espera"
                  : isFull ? "Entrar na espera" : "Confirmar presença"} para ${demoPlayers[0]}`}
              aria-pressed={attendees.includes(demoPlayers[0]) || waitlist.includes(demoPlayers[0])}
              className={`button ${attendees.includes(demoPlayers[0]) || waitlist.includes(demoPlayers[0]) ? "button-secondary" : "button-primary"}`}
              disabled={rosterIsLocked}
              onClick={() => handleAttendance(demoPlayers[0])}
              type="button"
            >
              {attendees.includes(demoPlayers[0])
                ? "Cancelar presença"
                : waitlist.includes(demoPlayers[0])
                  ? "Sair da espera"
                  : isFull ? "Entrar na espera" : "Confirmar presença"}
            </button>
          </div>
        ) : candidates.length ? candidates.map((player) => {
          const confirmed = attendees.includes(player);
          const waiting = waitlist.includes(player);
          const arrived = checkedIn.includes(player);
          const actionLabel = isCheckIn
            ? arrived ? "Desfazer check-in" : "Registrar check-in"
            : confirmed ? "Remover presença" : waiting ? "Sair da espera" : isFull ? "Entrar na espera" : "Confirmar presença";

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
                  aria-label={`${actionLabel} para ${player}`}
                  aria-pressed={arrived}
                  className={`button ${arrived ? "button-secondary" : "button-primary"}`}
                  disabled={rosterIsLocked}
                  onClick={() => handleCheckIn(player)}
                  type="button"
                >
                  {actionLabel}
                </button>
              ) : (
                <button
                  aria-label={`${actionLabel} para ${player}`}
                  aria-pressed={confirmed || waiting}
                  className={`button ${confirmed || waiting ? "button-secondary" : "button-primary"}`}
                  disabled={rosterIsLocked}
                  onClick={() => handleAttendance(player)}
                  type="button"
                >
                  {actionLabel}
                </button>
              )}
            </div>
          );
        }) : (
          <div className="match-demo-empty">
            <p>Confirme a presença de jogadores antes de fazer o check-in.</p>
            <Link className="button button-secondary" href="/presenca">Gerenciar confirmações</Link>
          </div>
        )}
      </div>

      {notice ? <p aria-live="polite" className="form-feedback" role="status">{notice}</p> : null}
      <div className="match-demo-links">
        {mode !== "presence" && !(isCheckIn && candidates.length === 0)
          ? <Link href="/presenca">Gerenciar confirmações</Link>
          : null}
        {mode !== "checkin" ? <Link href="/presenca/check-in">Ir para check-in</Link> : null}
        <span>Estado somente nesta sessão; recarregar a página reinicia a prévia.</span>
      </div>
    </div>
  );
}
