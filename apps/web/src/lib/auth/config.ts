export type SupabaseAuthConfig = {
  url: string;
  anonKey: string;
};

export function getSupabaseAuthConfig(): SupabaseAuthConfig | null {
  const url = process.env.SUPABASE_URL?.trim();
  const anonKey = process.env.SUPABASE_ANON_KEY?.trim();

  if (!url || !anonKey) {
    return null;
  }

  try {
    const parsedUrl = new URL(url);
    const isLocalDevelopment = ["localhost", "127.0.0.1"].includes(parsedUrl.hostname);
    if (parsedUrl.protocol !== "https:" && !isLocalDevelopment) {
      return null;
    }
  } catch {
    return null;
  }

  return { url, anonKey };
}
