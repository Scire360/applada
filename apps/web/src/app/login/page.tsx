import { Suspense } from "react";
import { Icon } from "@/components/icon";
import {
  recoverKeycloakPassword,
  signInWithKeycloak,
} from "@/app/actions/auth";
import { isAuthConfigured } from "@/auth";
import { safeCallbackPath } from "@/lib/callback-url";

type LoginPageProps = {
  searchParams: Promise<{ error?: string | string[]; callbackUrl?: string | string[] }>;
};

export default function LoginPage({ searchParams }: LoginPageProps) {
  return (
    <Suspense fallback={<main className="login-page" aria-busy="true" />}>
      <LoginContent searchParams={searchParams} />
    </Suspense>
  );
}

async function LoginContent({ searchParams }: LoginPageProps) {
  const { error, callbackUrl } = await searchParams;
  const returnTo = safeCallbackPath(callbackUrl);

  return (
    <main className="login-page">
      <div className="login-card">
        <section aria-label="Applada" className="login-story">
          <div className="login-brand">
            <span className="brand-mark">
              <Icon name="football" />
            </span>
            <span className="brand-wordmark">applada</span>
          </div>

          <div className="login-story-copy">
            <p className="login-overline">FUTEBOL AMADOR, DO SEU JEITO</p>
            <h1>
              O jogo fica melhor
              <br />
              <em>quando é junto.</em>
            </h1>
            <p>
              Seu time, suas partidas e tudo o que acontece entre um jogo e
              outro — em um só lugar.
            </p>
          </div>

          <p className="login-story-footer">
            Jogue junto. O próximo encontro começa aqui.
          </p>
        </section>

        <section aria-labelledby="login-title" className="login-panel">
          <div className="login-panel-content">
            <p className="login-overline">BEM-VINDO AO TIME</p>
            <h2 id="login-title">Entre no seu espaço.</h2>
            <p className="login-description">
              Acesse com segurança pela sua conta Applada.
            </p>

            {error ? (
              <p className="login-alert" role="alert">
                Não foi possível concluir o acesso. Tente novamente ou procure
                o administrador do seu espaço.
              </p>
            ) : null}

            {isAuthConfigured() ? (
              <>
                <form action={signInWithKeycloak}>
                  <input name="callbackUrl" type="hidden" value={returnTo} />
                  <button className="login-submit" type="submit">
                    Entrar com minha conta
                    <Icon name="arrow-right" />
                  </button>
                </form>

                <div className="login-account-actions">
                  <form action={recoverKeycloakPassword}>
                    <button className="login-text-action" type="submit">
                      Esqueci minha senha
                    </button>
                  </form>
                </div>

                <p className="login-provider-note">
                  A autenticação acontece em uma página segura do Keycloak. O
                  Applada nunca recebe nem armazena sua senha.
                </p>
                <p className="login-recovery-note">
                  Para recuperar a senha, selecione essa opção também na tela
                  segura do Keycloak.
                </p>
              </>
            ) : (
              <p className="login-alert" role="status">
                O acesso ainda não está configurado neste ambiente. Defina
                AUTH_SECRET e as variáveis AUTH_KEYCLOAK_* no arquivo local de
                ambiente seguindo o README.
              </p>
            )}
          </div>

          <p className="login-panel-footer">
            <Icon name="shield" />
            Acesso protegido por OpenID Connect
          </p>
        </section>
      </div>

      <p className="login-legal">© 2026 Applada · Futebol que aproxima.</p>
    </main>
  );
}
