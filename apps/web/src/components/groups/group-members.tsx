"use client";

import { useEffect, useState } from "react";
import { appApi } from "@/lib/api-client";
import type { GroupMember, GroupRole, Page } from "@/lib/connected-matches";
import { canLeave, memberActions } from "@/lib/groups";
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
  const { busy, error, notice, run, setNotice } = useAction();

  useEffect(() => {
    const controller = new AbortController();
    appApi<Page<GroupMember>>(listPath(groupId), { signal: controller.signal })
      .then(setPage)
      .catch((failure: unknown) => {
        if (!controller.signal.aborted) setLoadError(failure instanceof Error ? failure.message : "Não foi possível carregar os membros.");
      });
    return () => controller.abort();
  }, [groupId]);

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
    void run(async () => {
      await appApi<void>(`groups/${groupId}/members/${member.userId}`, { method: "DELETE" });
      await reload();
      setNotice("Membro removido.");
    });
  }

  function leave() {
    void run(async () => {
      await appApi<void>(`groups/${groupId}/members/${myUserId}`, { method: "DELETE" });
      onLeft();
    });
  }

  return (
    <section className="surface-card connected-card" aria-labelledby="membros" aria-busy={busy || (!page && !loadError)}>
      <h2 id="membros">Membros</h2>
      <div className="connected-feedback">
        <p role="status">{busy ? "Aguarde…" : notice}</p>
        {(error || loadError) && <p className="form-feedback form-feedback-error" role="alert">{error || loadError}</p>}
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
                    {actions.remove && (
                      <button aria-label={`Remover ${name}`} className="button button-secondary" disabled={busy} onClick={() => remove(member)} type="button">Remover</button>
                    )}
                  </span>
                )}
              </li>
            );
          })}
        </ul>
      )}
      {page?.nextCursor && <button className="button button-secondary" disabled={busy} onClick={more} type="button">Mais membros</button>}
      {canLeave(myRole) && <button className="button connected-danger" disabled={busy} onClick={leave} type="button">Sair do grupo</button>}
    </section>
  );
}
