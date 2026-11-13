import Link from "next/link";
import { AppShell } from "@/components/app-shell";
import { Icon } from "@/components/icon";

const matches = [
  { day: "19", weekday: "QUI", title: "Quinta de futebol", time: "19h30 – 21h", place: "Arena Santa Cecília · Campo 2", confirmed: "14 / 18", state: "Inscrições abertas", status: "open" },
  { day: "22", weekday: "DOM", title: "Desafio de domingo", time: "09h – 10h30", place: "Parque da Água Branca", confirmed: "8 / 16", state: "Inscrições abertas", status: "open" },
  { day: "26", weekday: "QUI", title: "Quinta de futebol", time: "19h30 – 21h", place: "Arena Santa Cecília · Campo 2", confirmed: "—", state: "Em organização", status: "planned" },
];

export default function MatchesPage() {
  return (
    <AppShell active="matches">
      <div className="module-page">
        <section className="page-heading">
          <div>
            <p className="eyebrow page-eyebrow">SANTA CECÍLIA FC · TEMPORADA 2026</p>
            <h1>Peladas que viram história.</h1>
            <p className="page-subtitle">Organize o próximo jogo. O resto é com o time.</p>
          </div>
          <div className="page-heading-actions">
            <a className="button button-secondary" href="#agenda">
              <Icon name="calendar" />
              Ver agenda
            </a>
            <Link className="button button-primary" href="/eventos/novo">
              <Icon name="plus" />
              Organizar pelada
            </Link>
          </div>
        </section>

        <section className="match-hero">
          <div className="match-hero-copy">
            <span className="match-status"><span /> INSCRIÇÕES ABERTAS</span>
            <p className="eyebrow">QUINTA-FEIRA, 19 DE NOVEMBRO</p>
            <h2>Quinta de futebol.</h2>
            <p>Uma noite, dois times e aquela energia que só a pelada tem.</p>
            <div className="match-details">
              <span><Icon name="clock" /> 19h30 – 21h</span>
              <span><Icon name="map-pin" /> Arena Santa Cecília · Campo 2</span>
            </div>
            <div className="match-capacity">
              <div className="capacity-label"><span><strong>14</strong> confirmados</span><span>4 vagas restantes</span></div>
              <div
                aria-label="Jogadores confirmados"
                aria-valuemax={18}
                aria-valuemin={0}
                aria-valuenow={14}
                aria-valuetext="14 de 18 jogadores confirmados"
                className="capacity-track"
                role="progressbar"
              >
                <span />
              </div>
            </div>
            <Link className="button button-light" href="/jogadores">Ver jogadores <Icon name="arrow-right" /></Link>
          </div>
          <div aria-hidden="true" className="match-hero-art">
            <span className="hero-art-circle" />
            <span className="hero-art-ball"><Icon name="football" /></span>
            <span className="hero-art-caption">JOGAR<br />JUNTO</span>
          </div>
        </section>

        <section className="surface-card schedule-card" id="agenda">
          <div className="section-title-row">
            <div>
              <p className="eyebrow">PRÓXIMOS ENCONTROS</p>
              <h2>Agenda do grupo</h2>
            </div>
            <span className="quiet-label">3 partidas programadas</span>
          </div>
          <div className="schedule-list">
            {matches.map((match) => (
              <article className="schedule-row" key={`${match.day}-${match.title}`}>
                <span className="date-tile"><span>{match.weekday}</span><strong>{match.day}</strong></span>
                <span className="schedule-info">
                  <strong>{match.title}</strong>
                  <span>{match.time} <span className="dot-separator">·</span> {match.place}</span>
                </span>
                <span className="schedule-attendance">{match.confirmed}<span>jogadores</span></span>
                <span className={`mini-pill ${match.status === "open" ? "pill-green" : "pill-neutral"}`}>{match.state}</span>
              </article>
            ))}
          </div>
        </section>
        <p className="demo-note">Prévia visual com dados ilustrativos. Inscrições, vagas e partidas ainda não estão conectadas à API.</p>
      </div>
    </AppShell>
  );
}
