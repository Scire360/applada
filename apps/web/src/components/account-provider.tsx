"use client";

import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { ApiError, backendApi, localAccountHeader, localApi, type ApiCall } from "@/lib/connected-matches";
import type { ApiMode } from "@/lib/api-mode";
import type { AvatarThumbnail } from "@/lib/avatar-photo";

type ReadyAccount = {
  state: "ready";
  userId: string | null;
  displayName: string | null;
  photo: string | null;
  photoState: "loading" | "ready" | "unavailable" | "unsupported";
};
type Account = ReadyAccount | { state: "loading" | "signed-out" | "unavailable" };
type StoredAccount = { displayName?: string; photo?: string };

const demoName = "Rafael Martins";
const storageKey = (userId: string | null) => `applada:account:${userId ?? "preview"}`;

function readStoredAccount(userId: string | null): StoredAccount {
  try {
    const saved: StoredAccount | null = JSON.parse(localStorage.getItem(storageKey(userId)) ?? "null");
    return {
      displayName: typeof saved?.displayName === "string" ? saved.displayName : undefined,
      photo: typeof saved?.photo === "string" && /^data:image\/(?:webp|png);base64,/.test(saved.photo) ? saved.photo : undefined,
    };
  } catch {
    return {};
  }
}

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

export function AccountProvider({ children, apiMode }: { children: ReactNode; apiMode: ApiMode }) {
  const [account, setAccount] = useState<Account>({ state: "loading" });
  const [open, setOpen] = useState(false);
  const [photoSaving, setPhotoSaving] = useState(false);
  const photoWrite = useRef(false);
  const previousFocus = useRef<HTMLButtonElement | null>(null);
  const photoUrl = account.state === "ready" ? account.photo : null;
  const api: ApiCall = apiMode === "backend" ? backendApi : localApi;

  useEffect(() => () => {
    if (photoUrl?.startsWith("blob:")) URL.revokeObjectURL(photoUrl);
  }, [photoUrl]);

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;

    async function load() {
      if (apiMode === "preview") {
        const saved = readStoredAccount(null);
        setAccount({ state: "ready", userId: null, displayName: saved.displayName ?? demoName, photo: saved.photo ?? null, photoState: "ready" });
        return;
      }

      let session: { userId: string };
      try {
        session = await api<{ userId: string }>("session", { signal });
      } catch (failure) {
        if (signal.aborted) return;
        setAccount({ state: failure instanceof ApiError && failure.status === 401 ? "signed-out" : "unavailable" });
        return;
      }

      try {
        const profile = await api<{ displayName: string | null }>("profile", {
          signal,
          headers: apiMode === "local" ? { [localAccountHeader]: session.userId } : undefined,
        });
        if (!signal.aborted) {
          setAccount({
            state: "ready",
            userId: session.userId,
            displayName: profile.displayName,
            photo: null,
            photoState: apiMode === "local" ? "loading" : "unsupported",
          });
        }
      } catch (failure) {
        if (!signal.aborted) {
          setAccount({ state: failure instanceof ApiError && failure.status === 401 ? "signed-out" : "unavailable" });
        }
        return;
      }

      if (apiMode !== "local") return;

      try {
        const response = await fetch("/api/local/avatar", {
          cache: "no-store", signal, headers: { [localAccountHeader]: session.userId },
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
  }, [api, apiMode]);

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

    let saved = name;
    if (account.userId) {
      const profile = await api<{ displayName: string }>("profile", {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          ...(apiMode === "local" ? { [localAccountHeader]: account.userId } : {}),
        },
        body: JSON.stringify({ displayName: name }),
      });
      saved = profile.displayName;
    } else if (apiMode === "preview") {
      try {
        localStorage.setItem(storageKey(null), JSON.stringify({ ...readStoredAccount(null), displayName: name }));
      } catch {
        throw new Error("O navegador não conseguiu salvar o nome da prévia.");
      }
    } else {
      throw new Error("O perfil ainda não está disponível.");
    }

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
      if (account.userId) {
        await api("avatar", photo ? {
          method: "PUT",
          headers: { "Content-Type": "application/octet-stream", [localAccountHeader]: account.userId },
          body: photo.pixels,
        } : { method: "DELETE", headers: { [localAccountHeader]: account.userId } });
      } else if (apiMode === "preview") {
        try {
          localStorage.setItem(storageKey(null), JSON.stringify({ ...readStoredAccount(null), photo: photo?.preview }));
        } catch {
          throw new Error("O navegador não conseguiu salvar a foto da prévia. Libere espaço e tente novamente.");
        }
      } else {
        throw new Error("A foto não está disponível nesta integração.");
      }
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
