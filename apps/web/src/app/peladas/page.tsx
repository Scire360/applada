import Link from "next/link";
import { AppShell } from "@/components/app-shell";
import { Icon } from "@/components/icon";
import { MatchSchedule, type ScheduledMatch } from "@/components/match-demo";
import campo from "./campo.module.css";

const matches: ScheduledMatch[] = [
  { date: "2026-11-19", title: "Quinta de futebol", time: "19h30 – 21h", place: "Arena Santa Cecília · Campo 2", confirmed: "14 / 18", state: "Inscrições abertas", status: "open" },
  { date: "2026-11-22", title: "Desafio de domingo", time: "09h – 10h30", place: "Parque da Água Branca", confirmed: "8 / 16", state: "Inscrições abertas", status: "open" },
  { date: "2026-11-26", title: "Quinta de futebol", time: "19h30 – 21h", place: "Arena Santa Cecília · Campo 2", confirmed: "—", state: "Em organização", status: "planned" },
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
              Ver agenda
            </a>
            <Link className="button button-secondary" href="/peladas/conectadas">
              <Icon name="calendar" />
              Partidas conectadas
            </Link>
            <Link className="button button-primary" href="/eventos/novo">
              <Icon name="plus" />
              Organizar pelada
            </Link>
          </div>
        </section>

        <section className={`match-hero ${campo.hero}`}>
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
          <div aria-hidden="true" className={campo.art}>
            <div className={campo.stadium}>
              <span className={campo.pitch}><span className={campo.goal} /><span className={campo.goal} /></span>
              <span className={campo.lens}><Icon name="football" /></span>
            </div>
          </div>
        </section>

        <section className="surface-card schedule-card" id="agenda">
          <div className="section-title-row">
            <div>
              <p className="eyebrow">PRÓXIMOS ENCONTROS</p>
              <h2>Agenda do grupo</h2>
            </div>
            <span className="quiet-label">Agenda do grupo e prévia desta sessão</span>
          </div>
          <div className="schedule-list">
            <MatchSchedule matches={matches} />
          </div>
        </section>
        <p className="demo-note">Prévia visual com dados ilustrativos. Inscrições, vagas e partidas ainda não estão conectadas à API.</p>
      </div>
    </AppShell>
  );
}
