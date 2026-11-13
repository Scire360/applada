import { AppShell } from "@/components/app-shell";
import { Icon } from "@/components/icon";

const transactions = [
  { initials: "LC", name: "Lucas Costa", detail: "Mensalidade · novembro", date: "12 nov", amount: "+ R$ 45,00", state: "Confirmado", color: "mint" },
  { initials: "BA", name: "Bruno Alves", detail: "Mensalidade · novembro", date: "11 nov", amount: "+ R$ 45,00", state: "Confirmado", color: "lavender" },
  { initials: "JP", name: "João Pedro", detail: "Rateio · Quinta de futebol", date: "10 nov", amount: "− R$ 180,00", state: "Despesa", color: "peach" },
];

const bars = [
  { month: "JUN", height: "43%" },
  { month: "JUL", height: "65%" },
  { month: "AGO", height: "56%" },
  { month: "SET", height: "76%" },
  { month: "OUT", height: "68%" },
  { month: "NOV", height: "88%" },
];

export default function FinancePage() {
  return (
    <AppShell active="finance">
      <div className="module-page finance-page">
        <section className="page-heading">
          <div>
            <p className="eyebrow page-eyebrow">SANTA CECÍLIA FC · TRANSPARÊNCIA DO GRUPO</p>
            <h1>As contas do time, em dia.</h1>
            <p className="page-subtitle">Acompanhe contribuições e despesas com clareza.</p>
          </div>
          <span className="button button-static"><Icon name="wallet" /> Novembro 2026</span>
        </section>

        <section aria-label="Resumo financeiro" className="finance-summary-grid">
          <article className="surface-card finance-balance">
            <span className="eyebrow">SALDO DO GRUPO</span>
            <p className="finance-total">R$ 1.284<span>,50</span></p>
            <span className="finance-caption">Disponível para as próximas partidas</span>
            <div className="balance-decoration" aria-hidden="true"><Icon name="wallet" /></div>
          </article>
          <article className="surface-card finance-stat">
            <span className="stat-icon stat-icon-green"><Icon name="arrow-down" /></span>
            <p className="eyebrow">ENTRADAS NO MÊS</p>
            <p className="stat-value">R$ 1.080<span>,00</span></p>
            <p className="stat-caption">24 contribuições registradas</p>
          </article>
          <article className="surface-card finance-stat">
            <span className="stat-icon stat-icon-peach"><Icon name="chart" /></span>
            <p className="eyebrow">DESPESAS NO MÊS</p>
            <p className="stat-value">R$ 720<span>,00</span></p>
            <p className="stat-caption">Quadra e materiais esportivos</p>
          </article>
        </section>

        <div className="finance-content-grid">
          <section className="surface-card transactions-card">
            <div className="section-title-row">
              <div>
                <p className="eyebrow">MOVIMENTAÇÕES</p>
                <h2>Atividade recente</h2>
              </div>
              <span className="quiet-label">Novembro</span>
            </div>
            <div className="transaction-list">
              {transactions.map((transaction) => (
                <article className="transaction-row" key={`${transaction.name}-${transaction.detail}`}>
                  <span className={`avatar avatar-soft avatar-${transaction.color}`}>{transaction.initials}</span>
                  <span className="transaction-copy"><strong>{transaction.name}</strong><span>{transaction.detail}</span></span>
                  <span className="transaction-date">{transaction.date}</span>
                  <span className="transaction-amount"><strong className={transaction.amount.startsWith("+") ? "positive" : ""}>{transaction.amount}</strong><span>{transaction.state}</span></span>
                </article>
              ))}
            </div>
            <p className="finance-footnote">Contribuições são exibidas como referência. Nenhum pagamento é processado nesta prévia.</p>
          </section>

          <section className="surface-card cashflow-card">
            <div className="section-title-row">
              <div>
                <p className="eyebrow">VISÃO DO GRUPO</p>
                <h2>Entradas ao longo do tempo</h2>
              </div>
              <span className="chart-period">6 meses</span>
            </div>
            <div aria-label="Entradas demonstrativas de junho a novembro" className="bar-chart" role="img">
              {bars.map((bar) => (
                <div className={`bar-column${bar.month === "NOV" ? " is-current" : ""}`} key={bar.month}>
                  <span className="bar-value" style={{ height: bar.height }} />
                  <span className="bar-label">{bar.month}</span>
                </div>
              ))}
            </div>
            <div className="chart-legend"><span /> Entradas registradas</div>
          </section>
        </div>
        <p className="demo-note">Prévia visual com valores ilustrativos. Pagamentos, cobranças e conciliação ainda não estão conectados ao backend.</p>
      </div>
    </AppShell>
  );
}
