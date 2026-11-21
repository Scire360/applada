"use client";

import Image from "next/image";
import Link from "next/link";
import { useEffect, useRef, useState, type ChangeEvent } from "react";
import { signOutOfApplada } from "@/app/actions/auth";
import { Icon } from "@/components/icon";
import { makeAvatarThumbnail, type AvatarThumbnail } from "@/lib/avatar-photo";
import { useAccount } from "./account-provider";
import { DisplayNameForm } from "./connected-profile";

function AccountAvatar({ name, photo, size = 36 }: { name?: string | null; photo?: string | null; size?: number }) {
  const initials = (name ?? "").trim().split(/\s+/u).filter(Boolean).slice(0, 2)
    .map((word) => Array.from(word)[0]).join("").toLocaleUpperCase("pt-BR");

  return (
    <span aria-hidden="true" className="avatar account-avatar" style={{ width: size, height: size }}>
      {photo ? <Image alt="" height={size} src={photo} unoptimized width={size} /> : initials || <Icon name="people" />}
    </span>
  );
}

export function HeaderAccountButton() {
  const { account, open, openAccount } = useAccount();
  const ready = account.state === "ready";
  return (
    <button
      aria-controls="conta-modal"
      aria-expanded={open}
      aria-haspopup="dialog"
      aria-label="Abrir conta"
      className="account-trigger"
      onClick={(event) => openAccount(event.currentTarget)}
      title={ready ? account.displayName ?? "Sua conta" : "Sua conta"}
      type="button"
    >
      <AccountAvatar name={ready ? account.displayName : null} photo={ready ? account.photo : null} />
    </button>
  );
}

export function SidebarAccountButton() {
  const { account, open, openAccount } = useAccount();
  const ready = account.state === "ready";
  return (
    <button
      aria-controls="conta-modal"
      aria-expanded={open}
      aria-haspopup="dialog"
      aria-label="Abrir conta pela barra lateral"
      className="sidebar-profile-link account-sidebar-trigger"
      onClick={(event) => openAccount(event.currentTarget)}
      type="button"
    >
      <AccountAvatar name={ready ? account.displayName : null} photo={ready ? account.photo : null} />
      <span className="profile-copy">
        <strong>{ready ? account.displayName ?? "Sua conta" : "Sua conta"}</strong>
        <span>{ready ? "Conta conectada" : "Perfil e configurações"}</span>
      </span>
      <Icon className="profile-chevron" name="chevron-down" />
    </button>
  );
}

function PhotoEditor() {
  const { account, photoSaving, savePhoto } = useAccount();
  const [draft, setDraft] = useState<AvatarThumbnail | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");
  const [error, setError] = useState("");

  if (account.state !== "ready") return null;
  const { displayName, photo, photoState } = account;
  const disabled = busy || photoSaving;

  async function selectFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    setBusy(true);
    setDraft(null);
    setError("");
    setNotice("");
    try {
      setDraft(await makeAvatarThumbnail(file));
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Não foi possível preparar a foto.");
    } finally {
      setBusy(false);
    }
  }

  async function applyPhoto(next: AvatarThumbnail | null) {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await savePhoto(next);
      setDraft(null);
      setNotice(next ? "Foto de perfil salva." : "Foto de perfil removida.");
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Não foi possível salvar a foto.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section aria-labelledby="conta-foto" className="account-photo">
      <AccountAvatar name={displayName} photo={draft?.preview ?? photo} size={72} />
      <div className="account-photo-info">
        <h3 id="conta-foto">Foto de perfil</h3>
        <p id="conta-foto-formatos">JPG, PNG ou WebP · até 1 MB</p>
        {photoState === "loading" && <p role="status">Carregando foto…</p>}
        {photoState === "unavailable" && <p role="status">O armazenamento de fotos está indisponível neste ambiente.</p>}
        {photoState === "ready" && (
          <div className="account-photo-actions">
            <label className="button button-secondary account-file-button">
              <Icon name="image" />
              <span>Escolher foto</span>
              <input accept="image/jpeg,image/png,image/webp" aria-describedby="conta-foto-formatos" disabled={disabled} onChange={selectFile} type="file" />
            </label>
            {draft ? (
              <>
                <button className="button button-primary" disabled={disabled} onClick={() => void applyPhoto(draft)} type="button">Salvar foto</button>
                <button className="account-text-action" disabled={disabled} onClick={() => setDraft(null)} type="button">Descartar</button>
              </>
            ) : photo && (
              <button className="account-text-action" disabled={disabled} onClick={() => void applyPhoto(null)} type="button">Remover foto</button>
            )}
          </div>
        )}
        <p className="account-feedback" role="status">{disabled ? "Aguarde…" : notice}</p>
        {error && <p className="form-feedback form-feedback-error" role="alert">{error}</p>}
      </div>
    </section>
  );
}

export function AccountDialog() {
  const { account, open, closeAccount } = useAccount();
  const ref = useRef<HTMLDialogElement>(null);
  const [section, setSection] = useState<"profile" | "settings">("profile");

  useEffect(() => {
    const dialog = ref.current;
    if (open && dialog && !dialog.open) dialog.showModal();
  }, [open]);

  function close() {
    ref.current?.close();
  }

  function onClose() {
    setSection("profile");
    closeAccount();
  }

  return (
    <dialog aria-labelledby="conta-titulo" className="account-dialog" id="conta-modal"
      onClick={(event) => { if (event.target === event.currentTarget) close(); }} onClose={onClose} ref={ref}>
      <div className="account-dialog-content">
        <header className="account-dialog-heading">
          <div>
            <h2 id="conta-titulo">Sua conta</h2>
            {account.state === "ready" && account.displayName && <p>{account.displayName}</p>}
          </div>
          <button aria-label="Fechar conta" className="account-close" onClick={close} type="button">
            <Icon name="close" />
          </button>
        </header>
        {open && <>
          <div aria-label="Seções da conta" className="account-sections" role="group">
            <button aria-pressed={section === "profile"} onClick={() => setSection("profile")} type="button"><Icon name="people" /> Perfil</button>
            <button aria-pressed={section === "settings"} onClick={() => setSection("settings")} type="button"><Icon name="settings" /> Configurações</button>
          </div>
          <div className="account-dialog-body">
            {section === "profile" ? (
              account.state === "ready" ? <>
                <PhotoEditor />
                <DisplayNameForm />
              </> : account.state === "signed-out" ? (
                <div className="account-empty">
                  <p>Entre na sua conta para editar o nome e a foto.</p>
                  <Link className="button button-primary" href="/login" onClick={close}>Entrar na conta</Link>
                </div>
              ) : <p role="status">{account.state === "loading" ? "Carregando perfil…" : "Não foi possível carregar o perfil. Atualize a página."}</p>
            ) : (
              <div className="account-settings">
                <h3>Preferências do grupo</h3>
                <Link href="/administracao/configuracoes" onClick={close}><Icon name="settings" /> Configurações do grupo <Icon name="arrow-right" /></Link>
                <Link href="/notificacoes" onClick={close}><Icon name="bell" /> Notificações <Icon name="arrow-right" /></Link>
                <div className="account-session">
                  <h3>Sessão</h3>
                  {account.state === "signed-out" ? <p>Você ainda não entrou.</p> : (
                    <form action={signOutOfApplada}>
                      <button className="button button-secondary" type="submit">Sair da sessão</button>
                    </form>
                  )}
                </div>
              </div>
            )}
          </div>
        </>}
      </div>
    </dialog>
  );
}
