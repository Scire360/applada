"use client";

import { useRef, useState } from "react";
import { ApiError } from "@/lib/api-client";

// Executa uma ação por vez e mantém aviso e erro para exibir em regiões aria-live.
export function useAction() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [signedOut, setSignedOut] = useState(false);
  const inFlight = useRef(false);

  async function run(action: () => Promise<void>) {
    if (inFlight.current) return;
    inFlight.current = true;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await action();
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 401) setSignedOut(true);
      setError(failure instanceof Error ? failure.message : "Não foi possível concluir a ação.");
    } finally {
      setBusy(false);
      inFlight.current = false;
    }
  }

  return { busy, error, notice, signedOut, run, setNotice, setError };
}

export const roleLabel = { owner: "Dono", admin: "Administrador", member: "Membro" } as const;
export const idPattern = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
