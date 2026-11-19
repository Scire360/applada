"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { safeReturnPath } from "@/lib/auth/return-path.mjs";
import { getSupabaseAuthConfig } from "@/lib/auth/config";
import { createSupabaseServerClient } from "@/lib/supabase/server";

export type LoginActionState = {
  error?: string;
};

export async function signIn(
  _previousState: LoginActionState,
  formData: FormData,
): Promise<LoginActionState> {
  const emailValue = formData.get("email");
  const passwordValue = formData.get("password");
  const email = typeof emailValue === "string" ? emailValue.trim().toLowerCase() : "";
  const password = typeof passwordValue === "string" ? passwordValue : "";

  if (!email || email.length > 254 || !password || password.length > 1024) {
    return { error: "E-mail ou senha inválidos." };
  }

  if (!getSupabaseAuthConfig()) {
    console.error("Supabase Auth is not configured for web sign-in.");
    return { error: "O acesso está temporariamente indisponível. Tente novamente mais tarde." };
  }

  const supabase = await createSupabaseServerClient();
  const { error } = await supabase.auth.signInWithPassword({ email, password });

  if (error) {
    return { error: "Não foi possível entrar com esses dados. Confira e tente novamente." };
  }

  revalidatePath("/", "layout");
  redirect(safeReturnPath(formData.get("next")));
}

export async function signOut() {
  if (!getSupabaseAuthConfig()) {
    console.error("Supabase Auth is not configured while signing out.");
    redirect("/login?status=configuration");
  }

  const supabase = await createSupabaseServerClient();
  const { error } = await supabase.auth.signOut();

  if (error) {
    console.error("Supabase sign-out failed.", { code: error.code ?? "unknown" });
    throw new Error("Unable to end the authenticated session.");
  }

  revalidatePath("/", "layout");
  redirect("/login");
}
