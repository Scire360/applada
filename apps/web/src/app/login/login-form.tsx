"use client";

import { useActionState } from "react";
import { signIn, type LoginActionState } from "./actions";

const initialState: LoginActionState = {};

export function LoginForm({ nextPath }: { nextPath: string }) {
  const [state, action, pending] = useActionState(signIn, initialState);

  return (
    <form action={action} className="auth-form">
      <input name="next" type="hidden" value={nextPath} />
      <label className="auth-field" htmlFor="email">
        <span>E-mail</span>
        <input
          autoComplete="username"
          autoCapitalize="none"
          autoCorrect="off"
          id="email"
          maxLength={254}
          name="email"
          placeholder="voce@exemplo.com"
          required
          type="email"
        />
      </label>

      <label className="auth-field" htmlFor="password">
        <span>Senha</span>
        <input
          autoComplete="current-password"
          id="password"
          maxLength={1024}
          name="password"
          placeholder="Sua senha"
          required
          type="password"
        />
      </label>

      {state.error ? (
        <p aria-live="polite" className="auth-error" role="alert">
          {state.error}
        </p>
      ) : null}

      <button className="button button-primary auth-submit" disabled={pending} type="submit">
        {pending ? "Entrando…" : "Entrar"}
      </button>
    </form>
  );
}
