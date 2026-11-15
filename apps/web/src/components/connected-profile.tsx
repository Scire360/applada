"use client";

import { useEffect, useState, type FormEvent } from "react";
import { localApi } from "@/lib/connected-matches";

type Phase = "loading" | "ready" | "saving" | "unavailable";

export function ConnectedProfile({ onSaved }: { onSaved: (displayName: string) => void }) {
  const [name, setName] = useState("");
  const [phase, setPhase] = useState<Phase>("loading");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");

  useEffect(() => {
    const controller = new AbortController();

    async function loadProfile() {
      try {
        const profile = await localApi<{ displayName: string | null }>("profile", { signal: controller.signal });
        if (!controller.signal.aborted) {
          setName(profile.displayName ?? "");
          setPhase("ready");
        }
      } catch {
        if (!controller.signal.aborted) {
          setPhase("unavailable");
          setError("Não foi possível carregar o perfil. Atualize a página para tentar novamente.");
        }
      }
    }

    void loadProfile();
    return () => controller.abort();
  }, []);

  async function saveProfile(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setPhase("saving");
    setError("");
    setNotice("");
    try {
      const profile = await localApi<{ displayName: string }>("profile", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ displayName: name }),
      });
      setName(profile.displayName);
      setNotice("Nome de exibição salvo.");
      onSaved(profile.displayName);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Não foi possível salvar o nome de exibição.");
    } finally {
      setPhase("ready");
    }
  }

  return (
    <section className="surface-card connected-card" aria-labelledby="meu-perfil-conectado">
      <h2 id="meu-perfil-conectado">Meu perfil</h2>
      <form className="preview-form" onSubmit={saveProfile}>
        <label className="preview-field">
          <span>Nome de exibição</span>
          <input disabled={phase !== "ready"} maxLength={80} name="displayName" onChange={(event) => setName(event.target.value)} required value={name} />
        </label>
        <button className="button button-primary" disabled={phase !== "ready"} type="submit">Salvar nome</button>
        <p role="status">{phase === "loading" ? "Carregando perfil…" : phase === "saving" ? "Salvando…" : notice}</p>
        {error && <p className="form-feedback form-feedback-error" role="alert">{error}</p>}
      </form>
    </section>
  );
}
