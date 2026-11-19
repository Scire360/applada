import Link from "next/link";
import { Suspense } from "react";
import { LoginForm } from "./login-form";
import { getSupabaseAuthConfig } from "@/lib/auth/config";
import { safeReturnPath } from "@/lib/auth/return-path.mjs";

type LoginPageProps = {
  searchParams: Promise<{ next?: string; status?: string }>;
};

export default function LoginPage({ searchParams }: LoginPageProps) {
  return (
    <Suspense fallback={<main className="auth-page"><p className="screen-loading" role="status">Carregando acesso…</p></main>}>
      <LoginContent searchParams={searchParams} />
    </Suspense>
  );
}

async function LoginContent({ searchParams }: LoginPageProps) {
  const params = await searchParams;
  const isConfigured = getSupabaseAuthConfig() !== null;
  const statusMessage = params.status === "configuration"
    ? "O acesso ainda precisa ser configurado pelo responsável do sistema."
    : params.status === "unavailable"
      ? "Não foi possível validar sua sessão agora. Tente novamente em instantes."
      : null;

  return (
    <main className="auth-page">
      <div className="auth-layout">
        <section aria-label="Applada" className="auth-story">
          <Link aria-label="Applada" className="auth-brand" href="/login">
            <span aria-hidden="true" className="auth-brand-mark">a</span>
            <span>applada</span>
          </Link>
          <div className="auth-story-copy">
            <p className="eyebrow">SEU FUTEBOL, MAIS PERTO</p>
            <h1>O jogo começa quando a gente se reúne.</h1>
            <p>Entre para organizar as partidas, encontrar seu time e viver cada momento dentro e fora de campo.</p>
          </div>
          <p className="auth-story-footer">Feito para quem vive o futebol amador.</p>
        </section>

        <section aria-labelledby="login-title" className="auth-panel">
          <div className="auth-card">
            <p className="eyebrow auth-eyebrow">ACESSO DE MEMBROS</p>
            <h2 id="login-title">Que bom ter você de volta.</h2>
            <p className="auth-description">Entre com o e-mail e a senha da sua conta Applada.</p>

            {statusMessage ? (
              <p className="auth-notice" role="status">{statusMessage}</p>
            ) : null}
            {!isConfigured ? (
              <p className="auth-notice" role="status">
                O login ainda não está configurado neste ambiente. Defina as variáveis do Supabase no servidor.
              </p>
            ) : null}

            {isConfigured ? <LoginForm nextPath={safeReturnPath(params.next)} /> : null}

            <p className="auth-help">
              Precisa de acesso? Fale com a organização do seu grupo.
            </p>
          </div>
          <p className="auth-legal">Acesse com segurança a conta do seu grupo no Applada.</p>
        </section>
      </div>
    </main>
  );
}
