"use client";

import { useState, type FormEvent } from "react";
import { appApi } from "@/lib/api-client";
import type { CreatedInvitation } from "@/lib/connected-matches";
import { useAction } from "./use-action";

export function GroupInvitation({ groupId }: { groupId: string }) {
  const [invitation, setInvitation] = useState<CreatedInvitation>();
  const { busy, error, notice, run, setNotice } = useAction();

  function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    void run(async () => {
      setInvitation(await appApi<CreatedInvitation>(`groups/${groupId}/invitations`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ expiresInHours: Number(data.get("validade")), maxUses: Number(data.get("usos")) }),
      }));
      setNotice("Convite criado. Copie o link agora: ele não será mostrado de novo.");
    });
  }

  function copy(link: string) {
    void run(async () => {
      await navigator.clipboard.writeText(link);
      setNotice("Link copiado.");
    });
  }

  function revoke(current: CreatedInvitation) {
    void run(async () => {
      await appApi<void>(`groups/${groupId}/invitations/${current.id}`, { method: "DELETE" });
      setInvitation(undefined);
      setNotice("Convite revogado.");
    });
  }

  const link = invitation ? `${window.location.origin}/convite#${invitation.token}` : "";

  return (
    <section className="surface-card connected-card" aria-labelledby="convite">
      <h2 id="convite">Convidar pessoas</h2>
      <form className="preview-form" onSubmit={create}>
        <fieldset className="connected-fieldset" disabled={busy}>
          <div className="preview-form-grid">
            <label className="preview-field">
              <span>Validade em horas</span>
              <input defaultValue={168} max={720} min={1} name="validade" required type="number" />
            </label>
            <label className="preview-field">
              <span>Número de usos</span>
              <input defaultValue={50} max={500} min={1} name="usos" required type="number" />
            </label>
          </div>
          <button className="button button-primary" type="submit">Criar convite</button>
        </fieldset>
      </form>
      <div className="connected-feedback">
        <p role="status">{busy ? "Aguarde…" : notice}</p>
        {error && <p className="form-feedback form-feedback-error" role="alert">{error}</p>}
      </div>
      {invitation && (
        <div className="group-invite-link">
          <label className="preview-field preview-field-wide">
            <span>Link do convite</span>
            <input onFocus={(event) => event.currentTarget.select()} readOnly value={link} />
          </label>
          <p>Vale até {new Intl.DateTimeFormat("pt-BR", { dateStyle: "medium", timeStyle: "short" }).format(new Date(invitation.expiresAt))} e para até {invitation.maxUses} {Number(invitation.maxUses) === 1 ? "pessoa" : "pessoas"}.</p>
          <div className="connected-actions">
            <button className="button button-primary" disabled={busy} onClick={() => copy(link)} type="button">Copiar</button>
            <button className="button button-secondary" disabled={busy} onClick={() => revoke(invitation)} type="button">Revogar</button>
          </div>
        </div>
      )}
    </section>
  );
}
