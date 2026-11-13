import { AppShell } from "@/components/app-shell";
import { Icon } from "@/components/icon";
import Link from "next/link";

const players = [
  { initials: "LC", name: "Lucas Costa", position: "Meio-campo", attendance: "96%", color: "mint", status: "Presente na quinta" },
  { initials: "BA", name: "Bruno Alves", position: "Atacante", attendance: "92%", color: "lavender", status: "Presente na quinta" },
  { initials: "JP", name: "João Pedro", position: "Goleiro", attendance: "88%", color: "peach", status: "Presente na quinta" },
  { initials: "MF", name: "Marcos Felipe", position: "Defensor", attendance: "84%", color: "blue", status: "A confirmar" },
  { initials: "AF", name: "André Ferreira", position: "Meio-campo", attendance: "81%", color: "yellow", status: "A confirmar" },
];

export default function PlayersPage() {
  return (
    <AppShell active="players">
      <div className="module-page">
        <section className="page-heading">
          <div>
            <p className="eyebrow page-eyebrow">SANTA CECÍLIA FC · ELENCO</p>
            <h1>O time é feito de pessoas.</h1>
            <p className="page-subtitle">Quem joga, quem chega e quem faz acontecer.</p>
          </div>
          <Link className="button button-primary" href="/jogadores/novo"><Icon name="plus" /> Convidar jogador</Link>
        </section>

        <section className="player-summary-grid">
          <article className="surface-card player-highlight">
            <span className="stat-icon stat-icon-green"><Icon name="people" /></span>
            <p className="eyebrow">ELENCO ATIVO</p>
            <p className="stat-value">24 <span>jogadores</span></p>
            <p className="stat-caption">Um grupo que sempre cresce quando a bola rola.</p>
          </article>
          <article className="surface-card player-highlight">
            <span className="stat-icon stat-icon-blue"><Icon name="chart" /></span>
            <p className="eyebrow">PRESENÇA MÉDIA</p>
            <p className="stat-value">87<span>%</span></p>
            <p className="stat-caption">O time está cada vez mais junto em campo.</p>
          </article>
          <article className="surface-card player-highlight player-quote">
            <span className="eyebrow">ESPÍRITO DE TIME</span>
            <h2>“A melhor escalação é todo mundo junto.”</h2>
            <span>Uma ideia do Santa Cecília FC</span>
          </article>
        </section>

        <section className="surface-card roster-card">
          <div className="section-title-row">
            <div>
              <p className="eyebrow">NOSSO ELENCO</p>
              <h2>Jogadores do grupo</h2>
            </div>
            <Link className="text-link" href="/jogadores/perfil">Ver perfil <Icon name="arrow-right" /></Link>
          </div>
          <div className="roster-table" role="table" aria-label="Jogadores do Santa Cecília FC">
            <div className="roster-header" role="row">
              <span role="columnheader">JOGADOR</span><span role="columnheader">POSIÇÃO</span><span role="columnheader">PRESENÇA</span><span role="columnheader">PRÓXIMO JOGO</span>
            </div>
            {players.map((player) => (
              <div className="roster-row" key={player.name} role="row">
                <span className="roster-person" role="cell">
                  <span className={`avatar avatar-soft avatar-${player.color}`}>{player.initials}</span>
                  <strong>{player.name}</strong>
                </span>
                <span className="roster-position" role="cell">{player.position}</span>
                <span className="roster-attendance" role="cell"><strong>{player.attendance}</strong><span>no mês</span></span>
                <span className="roster-status" role="cell"><span className={player.status.startsWith("Presente") ? "status-dot" : "status-dot status-dot-muted"} />{player.status}</span>
              </div>
            ))}
          </div>
        </section>
        <p className="demo-note">Prévia visual com perfis e estatísticas ilustrativos. Dados pessoais e presença ainda não estão conectados.</p>
      </div>
    </AppShell>
  );
}
