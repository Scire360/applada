import Link from "next/link";
import { AppShell } from "@/components/app-shell";
import { Icon } from "@/components/icon";

const activity = [
  { initials: "LC", name: "Lucas Costa", detail: "confirmou presença", time: "há 12 min", color: "mint" },
  { initials: "BA", name: "Bruno Alves", detail: "entrou para o grupo", time: "há 1 h", color: "lavender" },
  { initials: "RM", name: "Rafael Martins", detail: "publicou uma foto", time: "ontem", color: "peach" },
];

export default function Home() {
  return (
    <AppShell active="overview">
      <div className="dashboard-page">
        <section className="page-heading">
          <div>
            <p className="eyebrow page-eyebrow">SEXTA-FEIRA · SANTA CECÍLIA FC</p>
            <h1>O jogo começa por aqui.</h1>
            <p className="page-subtitle">Tudo em dia para reunir o time e fazer a bola rolar.</p>
          </div>
          <Link className="button button-primary" href="/peladas">
            <Icon name="plus" />
            Organizar pelada
          </Link>
        </section>

        <section aria-label="Resumo do seu espaço" className="stat-grid">
          <article className="stat-card">
            <div className="stat-card-top">
              <span className="stat-icon stat-icon-green"><Icon name="calendar" /></span>
              <span className="stat-overline">PRÓXIMA PELADA</span>
            </div>
            <p className="stat-value">Qui, 19h30</p>
            <p className="stat-caption">Arena Santa Cecília · Campo 2</p>
          </article>
          <article className="stat-card">
            <div className="stat-card-top">
              <span className="stat-icon stat-icon-blue"><Icon name="people" /></span>
              <span className="stat-overline">TIME CONFIRMADO</span>
            </div>
            <p className="stat-value">14 <span>de 18</span></p>
            <p className="stat-caption">4 vagas abertas para o próximo jogo</p>
          </article>
          <article className="stat-card">
            <div className="stat-card-top">
              <span className="stat-icon stat-icon-yellow"><Icon name="chart" /></span>
              <span className="stat-overline">PRESENÇA DO MÊS</span>
            </div>
            <p className="stat-value">87<span>%</span></p>
            <p className="stat-caption"><span className="positive">+6%</span> em relação ao mês passado</p>
          </article>
        </section>

        <section className="dashboard-grid">
          <article className="next-match-card">
            <div className="match-card-field" aria-hidden="true">
              <span className="field-line field-center" />
              <span className="field-line field-box-left" />
              <span className="field-line field-box-right" />
              <span className="field-circle" />
            </div>
            <div className="match-card-content">
              <div className="match-card-topline">
                <span className="match-status"><span /> PRÓXIMO JOGO</span>
                <span className="match-date">19 NOV · QUINTA-FEIRA</span>
              </div>
              <div className="match-card-title">
                <div>
                  <p className="eyebrow">SANTA CECÍLIA FC</p>
                  <h2>Quinta de futebol.</h2>
                </div>
                <span className="match-sport-icon"><Icon name="football" /></span>
              </div>
              <div className="match-details">
                <span><Icon name="clock" /> 19h30 – 21h</span>
                <span><Icon name="map-pin" /> Arena Santa Cecília · Campo 2</span>
              </div>
              <div className="match-capacity">
                <div className="capacity-label">
                  <span><strong>14</strong> confirmados</span>
                  <span>18 vagas</span>
                </div>
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
              <div className="match-card-footer">
                <div className="avatar-stack" aria-label="Alguns jogadores confirmados" role="img">
                  <span className="avatar avatar-photo avatar-one">LC</span>
                  <span className="avatar avatar-photo avatar-two">BA</span>
                  <span className="avatar avatar-photo avatar-three">JP</span>
                  <span className="avatar avatar-more">+11</span>
                </div>
                <Link className="text-link" href="/peladas">
                  Ver partida <Icon name="arrow-right" />
                </Link>
              </div>
            </div>
          </article>

          <article className="surface-card activity-card">
            <div className="section-title-row">
              <div>
                <p className="eyebrow">POR AQUI</p>
                <h2>Movimento do time</h2>
              </div>
              <Link aria-label="Ver comunidade" className="icon-link" href="/comunidade">
                <Icon name="arrow-right" />
              </Link>
            </div>
            <div className="activity-list">
              {activity.map((item) => (
                <div className="activity-item" key={item.name}>
                  <span className={`avatar avatar-soft avatar-${item.color}`}>{item.initials}</span>
                  <span className="activity-copy">
                    <span><strong>{item.name}</strong> {item.detail}</span>
                    <span className="activity-time">{item.time}</span>
                  </span>
                </div>
              ))}
            </div>
            <Link className="subtle-link" href="/comunidade">Acompanhar comunidade <Icon name="arrow-right" /></Link>
          </article>
        </section>

        <section className="bottom-grid">
          <article className="surface-card week-card">
            <div className="section-title-row">
              <div>
                <p className="eyebrow">AGENDA DO GRUPO</p>
                <h2>A semana em campo</h2>
              </div>
              <Link className="text-link" href="/peladas">Ver agenda <Icon name="arrow-right" /></Link>
            </div>
            <div className="week-list">
              <div className="week-item">
                <span className="date-tile"><span>QUI</span><strong>19</strong></span>
                <span className="week-game"><strong>Quinta de futebol</strong><span>19h30 · Arena Santa Cecília</span></span>
                <span className="mini-pill pill-green">14 confirmados</span>
              </div>
              <div className="week-item">
                <span className="date-tile"><span>DOM</span><strong>22</strong></span>
                <span className="week-game"><strong>Desafio de domingo</strong><span>09h · Parque da Água Branca</span></span>
                <span className="mini-pill pill-neutral">8 confirmados</span>
              </div>
            </div>
          </article>

          <article className="community-callout">
            <span className="callout-decoration" aria-hidden="true"><Icon name="sparkles" /></span>
            <p className="eyebrow">MAIS QUE FUTEBOL</p>
            <h2>A resenha também faz parte do jogo.</h2>
            <p>Fotos, histórias e aquele pós-jogo que merece ser lembrado.</p>
            <Link className="callout-link" href="/comunidade">Ir para a comunidade <Icon name="arrow-right" /></Link>
          </article>
        </section>
        <p className="demo-note">Esta é uma prévia visual. Os dados acima são ilustrativos e ainda não estão conectados ao sistema.</p>
      </div>
    </AppShell>
  );
}
