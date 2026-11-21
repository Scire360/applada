"use client";

import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { ApiError, accountHeader, appApi } from "@/lib/api-client";
import type { AvatarThumbnail } from "@/lib/avatar-photo";

type ReadyAccount = {
  state: "ready";
  userId: string;
  displayName: string | null;
  photo: string | null;
  photoState: "loading" | "ready" | "unavailable";
};
type Account = ReadyAccount | { state: "loading" | "signed-out" | "unavailable" };

type AccountContextValue = {
  account: Account;
  open: boolean;
  photoSaving: boolean;
  openAccount: (trigger: HTMLButtonElement) => void;
  closeAccount: () => void;
  saveName: (name: string) => Promise<void>;
  savePhoto: (photo: AvatarThumbnail | null) => Promise<void>;
};

const AccountContext = createContext<AccountContextValue | null>(null);

export function AccountProvider({ children }: { children: ReactNode }) {
  const [account, setAccount] = useState<Account>({ state: "loading" });
  const [open, setOpen] = useState(false);
  const [photoSaving, setPhotoSaving] = useState(false);
  const photoWrite = useRef(false);
  const previousFocus = useRef<HTMLButtonElement | null>(null);
  const photoUrl = account.state === "ready" ? account.photo : null;

  useEffect(() => () => {
    if (photoUrl?.startsWith("blob:")) URL.revokeObjectURL(photoUrl);
  }, [photoUrl]);

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;

    async function load() {
      let userId: string;
      let displayName: string | null;
      try {
        ({ userId } = await appApi<{ userId: string }>("me", { signal }));
        ({ displayName } = await appApi<{ displayName: string | null }>("profile", {
          signal, headers: { [accountHeader]: userId },
        }));
      } catch (failure) {
        if (!signal.aborted) {
          setAccount({ state: failure instanceof ApiError && failure.status === 401 ? "signed-out" : "unavailable" });
        }
        return;
      }
      if (signal.aborted) return;
      setAccount({ state: "ready", userId, displayName, photo: null, photoState: "loading" });

      try {
        const response = await fetch("/api/app/avatar", {
          cache: "no-store", signal, headers: { [accountHeader]: userId },
        });
        if (!response.ok) throw new Error("Storage indisponível.");
        const blob = response.status === 204 ? null : await response.blob();
        if (!signal.aborted) {
          const photo = blob ? URL.createObjectURL(blob) : null;
          setAccount((current) => current.state === "ready" ? { ...current, photo, photoState: "ready" } : current);
        }
      } catch {
        if (!signal.aborted) {
          setAccount((current) => current.state === "ready" ? { ...current, photoState: "unavailable" } : current);
        }
      }
    }

    void load();
    return () => controller.abort();
  }, []);

  function openAccount(trigger: HTMLButtonElement) {
    previousFocus.current = trigger;
    setOpen(true);
  }

  function closeAccount() {
    setOpen(false);
    previousFocus.current?.focus();
  }

  async function saveName(input: string) {
    if (account.state !== "ready") throw new Error("O perfil ainda não está disponível.");
    const name = input.trim();
    if (name.length === 0 || name.length > 80 || /\p{Cc}/u.test(name)) {
      throw new Error("Informe um nome de exibição de 1 a 80 caracteres.");
    }

    const profile = await appApi<{ displayName: string }>("profile", {
      method: "PUT",
      headers: { "Content-Type": "application/json", [accountHeader]: account.userId },
      body: JSON.stringify({ displayName: name }),
    });
    const saved = profile.displayName;

    setAccount((current) => current.state === "ready" && current.userId === account.userId
      ? { ...current, displayName: saved }
      : current);
  }

  async function savePhoto(photo: AvatarThumbnail | null) {
    if (account.state !== "ready" || account.photoState !== "ready") throw new Error("A foto ainda não está disponível.");
    if (photoWrite.current) throw new Error("Aguarde a alteração da foto em andamento.");

    photoWrite.current = true;
    setPhotoSaving(true);
    try {
      await appApi("avatar", photo ? {
        method: "PUT",
        headers: { "Content-Type": "application/octet-stream", [accountHeader]: account.userId },
        body: photo.pixels,
      } : { method: "DELETE", headers: { [accountHeader]: account.userId } });
      setAccount((current) => current.state === "ready" && current.userId === account.userId
        ? { ...current, photo: photo?.preview ?? null }
        : current);
    } finally {
      photoWrite.current = false;
      setPhotoSaving(false);
    }
  }

  return (
    <AccountContext.Provider value={{ account, open, photoSaving, openAccount, closeAccount, saveName, savePhoto }}>
      {children}
    </AccountContext.Provider>
  );
}

export function useAccount() {
  const value = useContext(AccountContext);
  if (!value) throw new Error("Use o perfil dentro de AccountProvider.");
  return value;
}
