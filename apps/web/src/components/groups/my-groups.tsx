"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState, type FormEvent } from "react";
import { ApiError, appApi } from "@/lib/api-client";
import type { GroupSummary, Page } from "@/lib/connected-matches";
import { roleLabel, useAction } from "./use-action";

const pageSize = 20;

function listPath(cursor?: string | null) {
  return `me/groups?limit=${pageSize}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`;
}

export function MyGroups() {
  const router = useRouter();
  const [page, setPage] = useState<Page<GroupSummary>>();
  const [loadError, setLoadError] = useState("");
  const [loadSignedOut, setLoadSignedOut] = useState(false);
  const { busy, error, signedOut, run } = useAction();

  useEffect(() => {
    const controller = new AbortController();
    appApi<Page<GroupSummary>>(listPath(), { signal: controller.signal })
      .then(setPage)
      .catch((failure: unknown) => {
        if (controller.signal.aborted) return;
        if (failure instanceof ApiError && failure.status === 401) setLoadSignedOut(true);
        setLoadError(failure instanceof ApiError ? failure.message : "Não foi possível carregar seus grupos.");
      });
    return () => controller.abort();
  }, []);

  function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const name = String(data.get("nome")).trim();
    const description = String(data.get("descricao")).trim();
    void run(async () => {
      const group = await appApi<GroupSummary>("groups", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name, description: description || null }),
      });
      router.push(`/grupos/${group.id}`);
    });
  }

  function more() {
    if (!page?.nextCursor) return;
    const cursor = page.nextCursor;
    void run(async () => {
      const next = await appApi<Page<GroupSummary>>(listPath(cursor));
      setPage((current) => ({ items: [...(current?.items ?? []), ...next.items], nextCursor: next.nextCursor }));
    });
  }

  if (loadSignedOut || signedOut) {
    return (
      <section className="surface-card connected-card" aria-labelledby="grupos-entrar">
        <h2 id="grupos-entrar">Entre para continuar</h2>
        <p>Entre na sua conta do Applada para ver e criar grupos.</p>
        <Link className="button button-primary" href="/login">Entrar</Link>
      </section>
    );
  }

  return (
    <div className="connected-grid">
      <section className="surface-card connected-card" aria-labelledby="meus-grupos" aria-busy={!page && !loadError}>
        <h2 id="meus-grupos">Meus grupos</h2>
        {!page && !loadError && <p role="status">Carregando seus grupos…</p>}
        {loadError && <p className="form-feedback form-feedback-error" role="alert">{loadError}</p>}
        {page && (page.items.length ? (
          <ul className="connected-list">
            {page.items.map((group) => (
              <li key={group.id}>
                <Link className="connected-match-item group-link" href={`/grupos/${group.id}`}>
                  <strong>{group.name}</strong>
                  <span>{roleLabel[group.myRole]} · {group.memberCount} {group.memberCount === 1 ? "membro" : "membros"}</span>
                </Link>
              </li>
            ))}
          </ul>
        ) : <p>Você ainda não participa de nenhum grupo. Crie o primeiro ao lado ou abra um link de convite.</p>)}
        {page?.nextCursor && (
          <button className="button button-secondary" disabled={busy} onClick={more} type="button">Mais grupos</button>
        )}
      </section>

      <section className="surface-card connected-card" aria-labelledby="criar-grupo">
        <h2 id="criar-grupo">Criar grupo</h2>
        <form className="preview-form" onSubmit={create}>
          <fieldset className="connected-fieldset" disabled={busy}>
            <div className="preview-form-grid">
              <label className="preview-field preview-field-wide">
                <span>Nome do grupo</span>
                <input maxLength={80} minLength={1} name="nome" placeholder="Ex.: Pelada de quinta" required />
              </label>
              <label className="preview-field preview-field-wide">
                <span>Descrição <small>(opcional)</small></span>
                <textarea maxLength={500} name="descricao" rows={3} />
              </label>
            </div>
            <button className="button button-primary" type="submit">Criar grupo</button>
          </fieldset>
        </form>
        <div className="connected-feedback">
          <p role="status">{busy ? "Aguarde…" : ""}</p>
          {error && <p className="form-feedback form-feedback-error" role="alert">{error}</p>}
        </div>
      </section>
    </div>
  );
}
