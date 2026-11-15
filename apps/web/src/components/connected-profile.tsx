"use client";

import { useState, type FormEvent } from "react";
import { useAccount } from "./account-provider";

export function DisplayNameForm() {
  const { account, saveName } = useAccount();
  const [draft, setDraft] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState("");
  const [error, setError] = useState("");

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (account.state !== "ready" || saving) return;
    setSaving(true);
    setError("");
    setNotice("");
    try {
      await saveName(draft ?? account.displayName ?? "");
      setDraft(null);
      setNotice("Nome de exibição salvo.");
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Não foi possível salvar o nome.");
    } finally {
      setSaving(false);
    }
  }

  const ready = account.state === "ready";
  const name = ready ? account.displayName ?? "" : "";

  return (
    <form className="account-name-form" onSubmit={submit}>
      <label className="preview-field">
        <span>Nome de exibição</span>
        <input
          disabled={!ready || saving}
          maxLength={80}
          name="displayName"
          onChange={(event) => { setDraft(event.target.value); setNotice(""); setError(""); }}
          required
          value={draft ?? name}
        />
      </label>
      <button className="button button-primary" disabled={!ready || saving} type="submit">
        {saving ? "Salvando…" : "Salvar nome"}
      </button>
      <p className="account-feedback" role="status">{account.state === "loading" ? "Carregando perfil…" : notice}</p>
      {account.state === "unavailable" && <p className="form-feedback form-feedback-error" role="alert">Não foi possível carregar o perfil.</p>}
      {error && <p className="form-feedback form-feedback-error" role="alert">{error}</p>}
    </form>
  );
}

export function ConnectedProfile() {
  return (
    <section className="surface-card connected-card" aria-labelledby="meu-perfil-conectado">
      <h2 id="meu-perfil-conectado">Meu perfil</h2>
      <DisplayNameForm />
    </section>
  );
}
