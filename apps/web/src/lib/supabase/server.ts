import { createServerClient } from "@supabase/ssr";
import { cookies } from "next/headers";
import { getSupabaseAuthConfig } from "@/lib/auth/config";
import { supabaseAuthCookieOptions } from "@/lib/auth/cookie-options";

export async function createSupabaseServerClient() {
  const config = getSupabaseAuthConfig();
  if (!config) {
    throw new Error("Supabase Auth configuration is missing or invalid.");
  }

  const cookieStore = await cookies();

  return createServerClient(config.url, config.anonKey, {
    cookieOptions: supabaseAuthCookieOptions,
    cookies: {
      getAll() {
        return cookieStore.getAll();
      },
      setAll(cookiesToSet) {
        for (const { name, value, options } of cookiesToSet) {
          cookieStore.set(name, value, options);
        }
      },
    },
  });
}
