"use server";

import { signIn, signOut } from "@/auth";

export async function signInWithKeycloak() {
  await signIn("keycloak", { redirectTo: "/" }, { kc_locale: "pt-BR" });
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
