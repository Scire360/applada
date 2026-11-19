import Link from "next/link";

export default function AccessDeniedPage() {
  return (
    <main className="login-page">
      <div className="login-card">
        <section aria-label="Applada" className="login-story">
          <span className="brand-wordmark">applada</span>
          <div className="login-story-copy">
            <p className="login-overline">ACESSO SEGURO</p>
            <h1>
              Cada pessoa
              <br />
              no seu espaço.
            </h1>
            <p>
              O acesso às áreas administrativas é restrito a contas autorizadas.
            </p>
          </div>
          <p className="login-story-footer">
            Jogue junto. O próximo encontro começa aqui.
          </p>
        </section>

        <section aria-labelledby="access-denied-title" className="login-panel">
          <div className="login-panel-content">
            <p className="login-overline">ÁREA RESTRITA</p>
            <h2 id="access-denied-title">Acesso não autorizado.</h2>
            <p className="login-description">
              Sua conta não tem permissão para acessar esta área.
            </p>
            <Link className="login-submit" href="/">
              Voltar ao início
            </Link>
          </div>
          <p className="login-panel-footer">
            Acesso administrativo protegido por função
          </p>
        </section>
      </div>

      <p className="login-legal">© 2026 Applada · Futebol que aproxima.</p>
    </main>
  );
}
