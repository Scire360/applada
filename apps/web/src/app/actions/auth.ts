"use server";

import { signIn, signOut } from "@/auth";
import { safeCallbackPath } from "@/lib/callback-url";

// callbackUrl vem do formulário do /login; só caminho relativo da própria origem é aceito.
export async function signInWithKeycloak(formData: FormData) {
  await signIn("keycloak", { redirectTo: safeCallbackPath(formData.get("callbackUrl")) }, { kc_locale: "pt-BR" });
}

export async function registerWithKeycloak() {
  await signIn(
    "keycloak",
    { redirectTo: "/" },
    { prompt: "create", kc_locale: "pt-BR" },
  );
}

export async function recoverKeycloakPassword() {
  await signIn(
    "keycloak",
    { redirectTo: "/" },
    { prompt: "login", kc_locale: "pt-BR" },
  );
}

export async function signOutOfApplada() {
  await signOut({ redirectTo: "/login" });
}

// Volta para /convite?retomar=1 depois do login; o BFF usa o convite guardado no cookie.
export async function signInForInvitation() {
  await signIn("keycloak", { redirectTo: "/convite?retomar=1" }, { kc_locale: "pt-BR" });
}
