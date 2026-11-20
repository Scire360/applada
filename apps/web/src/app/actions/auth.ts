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
