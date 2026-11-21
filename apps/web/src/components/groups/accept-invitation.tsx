"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { signInForInvitation } from "@/app/actions/auth";
import { ApiError, appApi } from "@/lib/api-client";
import { keepInvitation } from "@/lib/groups";
import { useAction } from "./use-action";

type Phase = "checking" | "incomplete" | "signed-out" | "ready" | "error";

export function AcceptInvitation() {
  const router = useRouter();
  const [phase, setPhase] = useState<Phase>("checking");
  const [loadError, setLoadError] = useState("");
  // O token sai da barra de endereço, então fica só em memória. Com ?retomar=1 ele está no cookie do BFF.
  const token = useRef<string | null>(null);
  const resuming = useRef(false);
  const { busy, error, run } = useAction();

  useEffect(() => {
    const controller = new AbortController();
    const url = new URL(window.location.href);
    token.current = keepInvitation(token.current, url.hash);
    resuming.current = resuming.current || url.searchParams.get("retomar") === "1";
    window.history.replaceState(null, "", `${url.pathname}${url.search}`);
    if (!token.current && !resuming.current) {
      setPhase("incomplete");
      return;
    }
    appApi("me", { signal: controller.signal })
      .then(() => setPhase("ready"))
      .catch((failure: unknown) => {
        if (controller.signal.aborted) return;
        if (failure instanceof ApiError && failure.status === 401) {
          setPhase("signed-out");
        } else {
          setLoadError(failure instanceof ApiError ? failure.message : "Não foi possível conectar agora.");
          setPhase("error");
        }
      });
    return () => controller.abort();
  }, []);

  function accept() {
    void run(async () => {
      try {
        const result = await appApi<{ groupId: string }>("group-invitations/acceptance", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(token.current ? { token: token.current } : {}),
        });
        router.push(`/grupos/${result.groupId}`);
      } catch (failure) {
        // Sessão expirada no meio do aceite: volta para a tela de entrada, que guarda o convite e inicia o login.
        if (failure instanceof ApiError && failure.status === 401) setPhase("signed-out");
        if (failure instanceof ApiError && (failure.status === 400 || failure.status === 404)) throw new ApiError(failure.status, "Convite inválido ou expirado.");
        if (failure instanceof ApiError && failure.status === 409) throw new ApiError(409, "Este grupo está cheio.");
        throw failure;
      }
    });
  }

  function signIn() {
    void run(async () => {
      if (token.current) {
        await appApi<void>("invitation/pending", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ token: token.current }),
        });
      }
      await signInForInvitation();
    });
  }

  return (
    <section className="surface-card connected-card invite-card" aria-labelledby="convite-titulo" aria-busy={phase === "checking"}>
      <h1 id="convite-titulo">Convite para um grupo</h1>
      {phase === "checking" && <p role="status">Verificando o convite…</p>}
      {phase === "incomplete" && <p className="form-feedback form-feedback-error" role="alert">Link de convite incompleto.</p>}
      {phase === "error" && (
        <>
          <p className="form-feedback form-feedback-error" role="alert">{loadError}</p>
          <button className="button button-primary" onClick={() => window.location.reload()} type="button">Tentar novamente</button>
        </>
      )}
      {phase === "signed-out" && (
        <>
          <p>Para entrar no grupo, primeiro entre na sua conta do Applada. Depois do login você volta para esta página.</p>
          <button className="button button-primary" disabled={busy} onClick={signIn} type="button">Entrar na minha conta</button>
        </>
      )}
      {phase === "ready" && (
        <>
          <p>Você foi convidado para participar de um grupo no Applada.</p>
          <button className="button button-primary" disabled={busy} onClick={accept} type="button">Entrar no grupo</button>
        </>
      )}
      <div className="connected-feedback">
        <p role="status">{busy ? "Aguarde…" : ""}</p>
        {error && <p className="form-feedback form-feedback-error" role="alert">{error}</p>}
      </div>
    </section>
  );
}
