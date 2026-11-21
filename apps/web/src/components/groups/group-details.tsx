"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { ApiError, appApi } from "@/lib/api-client";
import type { GroupDetails as Group } from "@/lib/connected-matches";
import { canManageInvitations } from "@/lib/groups";
import { GroupInvitation } from "./group-invitation";
import { GroupMatches } from "./group-matches";
import { GroupMembers } from "./group-members";
import { SignInAgain } from "./sign-in-again";
import { idPattern, roleLabel } from "./use-action";

type State =
  | { phase: "loading" }
  | { phase: "signed-out" }
  | { phase: "not-found" }
  | { phase: "error"; message: string }
  | { phase: "ready"; group: Group; userId: string };

export function GroupDetails({ id }: { id: string }) {
  const router = useRouter();
  const valid = idPattern.test(id);
  const [state, setState] = useState<State>({ phase: "loading" });

  const loadGroup = useCallback(async (signal?: AbortSignal) => {
    const [me, group] = await Promise.all([
      appApi<{ userId: string }>("me", { signal }),
      appApi<Group>(`groups/${id}`, { signal }),
    ]);
    return { group, userId: me.userId };
  }, [id]);

  useEffect(() => {
    if (!valid) return;
    const controller = new AbortController();
    loadGroup(controller.signal)
      .then((loaded) => setState({ phase: "ready", ...loaded }))
      .catch((failure: unknown) => {
        if (controller.signal.aborted) return;
        if (failure instanceof ApiError && failure.status === 401) setState({ phase: "signed-out" });
        else if (failure instanceof ApiError && failure.status === 404) setState({ phase: "not-found" });
        else setState({ phase: "error", message: failure instanceof ApiError ? failure.message : "Não foi possível carregar o grupo." });
      });
    return () => controller.abort();
  }, [loadGroup, valid]);

  // Atualiza só os dados do grupo (contagem de membros e meu papel) depois de mudanças nos membros.
  function refreshGroup() {
    loadGroup()
      .then((loaded) => setState({ phase: "ready", ...loaded }))
      .catch(() => undefined);
  }

  if (!valid || state.phase === "not-found") {
    return (
      <section className="surface-card connected-card" aria-labelledby="grupo-erro">
        <h1 id="grupo-erro">Grupo não encontrado.</h1>
        <Link className="button button-primary" href="/grupos">Voltar para meus grupos</Link>
      </section>
    );
  }
  if (state.phase === "signed-out") {
    return (
      <section className="surface-card connected-card" aria-labelledby="grupo-entrar">
        <h1 id="grupo-entrar">Entre para continuar</h1>
        <p>Entre na sua conta do Applada para ver este grupo.</p>
        <SignInAgain />
      </section>
    );
  }
  if (state.phase === "error") {
    return (
      <section className="surface-card connected-card" aria-labelledby="grupo-erro">
        <h1 id="grupo-erro">Grupo</h1>
        <p className="form-feedback form-feedback-error" role="alert">{state.message}</p>
        <button className="button button-primary" onClick={() => window.location.reload()} type="button">Tentar novamente</button>
      </section>
    );
  }
  if (state.phase === "loading") return <p role="status">Carregando o grupo…</p>;

  const { group, userId } = state;
  return (
    <>
      <section className="page-heading">
        <div>
          <h1>{group.name}</h1>
          <p className="page-subtitle">
            {group.description ? `${group.description} · ` : ""}{roleLabel[group.myRole]} · {group.memberCount} {group.memberCount === 1 ? "membro" : "membros"}
          </p>
        </div>
        <Link className="button button-secondary" href="/grupos">Voltar para meus grupos</Link>
      </section>
      <div className="connected-grid">
        <GroupMembers groupId={group.id} myRole={group.myRole} myUserId={userId} onChanged={refreshGroup} onLeft={() => router.push("/grupos")} />
        <div className="group-column">
          <GroupMatches groupId={group.id} myRole={group.myRole} myUserId={userId} />
          {canManageInvitations(group.myRole) && <GroupInvitation groupId={group.id} />}
        </div>
      </div>
    </>
  );
}
