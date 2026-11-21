import "server-only";

import { getToken } from "next-auth/jwt";
import type { NextRequest } from "next/server";

const production = () => process.env.NODE_ENV === "production";

// Access token do Keycloak guardado no JWT criptografado do Auth.js. Fica só no servidor:
// nunca vai para resposta, URL, storage ou log.
export async function getAccessToken(request: NextRequest): Promise<string | null> {
  const secret = process.env.AUTH_SECRET;
  if (!secret) return null;
  // Só o cookie da sessão conta: o getToken também aceitaria um JWT no cabeçalho Authorization.
  const token = await getToken({
    req: { headers: { cookie: request.headers.get("cookie") ?? "" } },
    secret,
    secureCookie: production(),
  });
  return typeof token?.keycloakAccessToken === "string" && token.keycloakAccessToken ? token.keycloakAccessToken : null;
}

// Origem da API .NET (APPLADA_API_URL). Em produção precisa ser HTTPS.
export function apiOrigin(): string {
  const configured = process.env.APPLADA_API_URL ?? (production() ? undefined : "http://localhost:5080");
  if (!configured) throw new Error("APPLADA_API_URL precisa estar configurada.");
  const url = new URL(configured);
  if (production() && url.protocol !== "https:") throw new Error("APPLADA_API_URL precisa usar HTTPS em produção.");
  if (url.protocol !== "https:" && url.protocol !== "http:") throw new Error("APPLADA_API_URL precisa ser HTTP(S).");
  return url.origin;
}

// Origem pública do site (AUTH_URL), usada para conferir o Origin das escritas.
export function publicOrigin(): string {
  const configured = process.env.AUTH_URL ?? process.env.NEXTAUTH_URL ?? (production() ? undefined : "http://localhost:3000");
  if (!configured) throw new Error("AUTH_URL precisa estar configurada.");
  return new URL(configured).origin;
}
