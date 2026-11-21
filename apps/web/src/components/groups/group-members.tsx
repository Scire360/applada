"use client";

import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { ApiError, appApi } from "@/lib/api-client";
import type { GroupMember, GroupRole, Page } from "@/lib/connected-matches";
import { canLeave, memberActions } from "@/lib/groups";
import { SignInAgain } from "./sign-in-again";
import { roleLabel, useAction } from "./use-action";

function listPath(groupId: string, cursor?: string | null) {
  return `groups/${groupId}/members${cursor ? `?cursor=${encodeURIComponent(cursor)}` : ""}`;
}

export function GroupMembers({ groupId, myRole, myUserId, onChanged, onLeft }: {
  groupId: string;
  myRole: GroupRole;
  myUserId: string;
  onChanged: () => void;
  onLeft: () => void;
}) {
  const [page, setPage] = useState<Page<GroupMember>>();
  const [loadError, setLoadError] = useState("");
  const { busy, error, notice, signedOut, run, setNotice, setSignedOut } = useAction();
  // Confirmação em dois passos: guarda quem está para ser removido ("leave" = eu saio do grupo).
  const [confirming, setConfirming] = useState<string | null>(null);
  const confirmButton = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (confirming) confirmButton.current?.focus();
  }, [confirming]);

  useEffect(() => {
    const controller = new AbortController();
    appApi<Page<GroupMember>>(listPath(groupId), { signal: controller.signal })
      .then(setPage)
      .catch((failure: unknown) => {
        if (controller.signal.aborted) return;
        if (failure instanceof ApiError && failure.status === 401) setSignedOut(true);
        setLoadError(failure instanceof ApiError ? failure.message : "Não foi possível carregar os membros.");
      });
    return () => controller.abort();
  }, [groupId, setSignedOut]);

  async function reload() {
    setPage(await appApi<Page<GroupMember>>(listPath(groupId)));
    onChanged();
  }

  function more() {
    if (!page?.nextCursor) return;
    const cursor = page.nextCursor;
    void run(async () => {
      const next = await appApi<Page<GroupMember>>(listPath(groupId, cursor));
      setPage((current) => ({ items: [...(current?.items ?? []), ...next.items], nextCursor: next.nextCursor }));
    });
  }

  function changeRole(member: GroupMember, role: "admin" | "member") {
    void run(async () => {
      await appApi<GroupMember>(`groups/${groupId}/members/${member.userId}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ role }),
      });
      await reload();
      setNotice(role === "admin" ? "Membro promovido a administrador." : "Administrador voltou a ser membro.");
    });
  }

  function remove(member: GroupMember) {
    setConfirming(null);
    void run(async () => {
      await appApi<void>(`groups/${groupId}/members/${member.userId}`, { method: "DELETE" });
      await reload();
      setNotice("Membro removido.");
    });
  }

  function leave() {
    setConfirming(null);
    void run(async () => {
      await appApi<void>(`groups/${groupId}/members/${myUserId}`, { method: "DELETE" });
      onLeft();
    });
  }

  function cancelOnEscape(event: KeyboardEvent<HTMLElement>) {
    if (event.key === "Escape") setConfirming(null);
  }

  return (
    <section className="surface-card connected-card" aria-labelledby="membros" aria-busy={busy || (!page && !loadError)}>
      <h2 id="membros">Membros</h2>
      <div className="connected-feedback">
        <p role="status">{busy ? "Aguarde…" : notice}</p>
        {(error || loadError) && <p className="form-feedback form-feedback-error" role="alert">{error || loadError}</p>}
        {signedOut && <SignInAgain />}
      </div>
      {!page && !loadError && <p role="status">Carregando membros…</p>}
      {page && (
        <ul className="group-members">
          {page.items.map((member) => {
            const name = member.displayName ?? "Pessoa sem nome";
            const actions = memberActions(myRole, myUserId, member);
            return (
              <li className="group-member" key={member.userId}>
                <span className="group-member-name">
                  <strong>{name}{member.userId === myUserId ? " (você)" : ""}</strong>
                  <span>{roleLabel[member.role]}</span>
                </span>
                {(actions.role || actions.remove) && (
                  <span className="connected-actions">
                    {actions.role && (
                      <button aria-label={`${actions.role === "admin" ? "Promover" : "Rebaixar"} ${name}`} className="button button-secondary" disabled={busy} onClick={() => changeRole(member, actions.role!)} type="button">
                        {actions.role === "admin" ? "Promover a administrador" : "Rebaixar a membro"}
                      </button>
                    )}
                    {actions.remove && (confirming === member.userId ? (
                      <span className="connected-actions" onKeyDown={cancelOnEscape}>
                        <button className="button connected-danger" disabled={busy} onClick={() => remove(member)} ref={confirmButton} type="button">Confirmar remoção de {name}</button>
                        <button className="button button-secondary" onClick={() => setConfirming(null)} type="button">Cancelar</button>
                      </span>
                    ) : (
                      <button aria-label={`Remover ${name}`} className="button button-secondary" disabled={busy} onClick={() => setConfirming(member.userId)} type="button">Remover</button>
                    ))}
                  </span>
                )}
              </li>
            );
          })}
        </ul>
      )}
      {page?.nextCursor && <button className="button button-secondary" disabled={busy} onClick={more} type="button">Mais membros</button>}
      {canLeave(myRole) && (confirming === "leave" ? (
        <div className="connected-actions" onKeyDown={cancelOnEscape}>
          <button className="button connected-danger" disabled={busy} onClick={leave} ref={confirmButton} type="button">Confirmar saída</button>
          <button className="button button-secondary" onClick={() => setConfirming(null)} type="button">Cancelar</button>
        </div>
      ) : (
        <button className="button connected-danger" disabled={busy} onClick={() => setConfirming("leave")} type="button">Sair do grupo</button>
      ))}
    </section>
  );
}
