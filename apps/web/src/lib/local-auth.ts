import "server-only";

import { createHash, randomBytes, timingSafeEqual } from "node:crypto";
import { NextRequest, NextResponse } from "next/server";

export const webOrigin = "http://localhost:3000";
export const apiOrigin = "http://localhost:5080";
const issuer = "http://localhost:8080/realms/applada";
const clientId = "applada-dev-web";
const callback = webOrigin + "/api/local/callback";
const connectedPage = "/peladas/conectadas";
const cookiePath = "/api/local";
const flowCookie = "__Secure-applada-flow";
export const sessionCookie = "__Secure-applada-session";
const flowSeconds = 300;
const maxSessionSeconds = 900;
const maxEntries = 128;

type Expiring = { expires: number };
type Login = Expiring & { verifier: string; state: string; returnTo: string };
type Session = Expiring & { token: string };
type Store = { logins: Map<string, Login>; sessions: Map<string, Session> };
const local = globalThis as typeof globalThis & { appladaLocalAuth?: Store };
const store = local.appladaLocalAuth ??= { logins: new Map(), sessions: new Map() };

export function localIntegrationEnabled(request: NextRequest) {
  return process.env.NODE_ENV === "development"
    && process.env.APPLADA_LOCAL_INTEGRATION === "1"
    && request.headers.get("host") === "localhost:3000";
}

export function secureResponse(response: NextResponse) {
  response.headers.set("Cache-Control", "private, no-store");
  response.headers.set("Referrer-Policy", "no-referrer");
  response.headers.set("X-Content-Type-Options", "nosniff");
  return response;
}

export function localError(status: number, title: string) {
  return secureResponse(NextResponse.json({ status, title }, {
    status,
    headers: { "Content-Type": "application/problem+json" },
  }));
}

function redirect(destination: string | URL) {
  return secureResponse(NextResponse.redirect(new URL(destination, webOrigin), 303));
}

function setCookie(response: NextResponse, name: string, value: string, maxAge: number) {
  response.cookies.set(name, value, { path: cookiePath, httpOnly: true, sameSite: "lax", secure: true, maxAge });
}

function opaqueId() {
  return randomBytes(32).toString("base64url");
}

function readEntry<T extends Expiring>(entries: Map<string, T>, id: string) {
  const entry = entries.get(id);
  if (entry && entry.expires > Date.now()) return entry;
  entries.delete(id);
}

function remember<T extends Expiring>(entries: Map<string, T>, id: string, entry: T) {
  for (const [key, item] of entries) {
    if (item.expires <= Date.now()) entries.delete(key);
  }
  if (entries.size >= maxEntries) return false;
  entries.set(id, entry);
  return true;
}

export const matchIdPattern = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

export function beginLogin(request: NextRequest) {
  const id = opaqueId();
  const verifier = opaqueId();
  const state = opaqueId();
  const matchId = request.nextUrl.searchParams.get("partida");
  const returnTo = matchId && matchIdPattern.test(matchId)
    ? `${connectedPage}?partida=${matchId}` : connectedPage;

  store.logins.delete(request.cookies.get(flowCookie)?.value ?? "");
  if (!remember(store.logins, id, { verifier, state, returnTo, expires: Date.now() + flowSeconds * 1000 })) {
    return localError(429, "Muitas sessões de login em andamento. Aguarde e tente novamente.");
  }

  const url = new URL(issuer + "/protocol/openid-connect/auth");
  url.search = new URLSearchParams({
    client_id: clientId,
    response_type: "code",
    redirect_uri: callback,
    scope: "openid",
    state,
    code_challenge: createHash("sha256").update(verifier).digest("base64url"),
    code_challenge_method: "S256",
    prompt: "login",
  }).toString();
  const response = redirect(url);
  setCookie(response, flowCookie, id, flowSeconds);
  return response;
}

function sameState(actual: string | null, expected: string) {
  const value = Buffer.from(actual ?? "");
  const wanted = Buffer.from(expected);
  return value.length === wanted.length && timingSafeEqual(value, wanted);
}

export async function completeLogin(request: NextRequest) {
  const flowId = request.cookies.get(flowCookie)?.value ?? "";
  const login = readEntry(store.logins, flowId);
  const code = request.nextUrl.searchParams.get("code");
  const state = request.nextUrl.searchParams.get("state");
  const fail = () => {
    const url = new URL(login?.returnTo ?? connectedPage, webOrigin);
    url.searchParams.set("login", "falhou");
    const response = redirect(url);
    setCookie(response, flowCookie, "", 0);
    return response;
  };
  if (!login || !sameState(state, login.state)) return fail();
  store.logins.delete(flowId);
  if (!code || code.length > 2048 || request.nextUrl.searchParams.get("iss") !== issuer) return fail();

  try {
    const exchange = await fetch(issuer + "/protocol/openid-connect/token", {
      method: "POST",
      body: new URLSearchParams({ grant_type: "authorization_code", client_id: clientId, code, redirect_uri: callback, code_verifier: login.verifier }),
      redirect: "error",
      cache: "no-store",
      signal: AbortSignal.timeout(8000),
    });
    if (!exchange.ok) return fail();
    const { access_token, expires_in, token_type } = await exchange.json();
    if (typeof access_token !== "string" || !access_token || access_token.length > 8192
        || token_type !== "Bearer" || !Number.isInteger(expires_in) || expires_in <= 5) return fail();

    // Identity comes from the API, which verifies the access token and resolves its issuer/sub.
    const identity = await fetch(apiOrigin + "/api/v1/me", {
      headers: { Authorization: `Bearer ${access_token}` },
      redirect: "error",
      cache: "no-store",
      signal: AbortSignal.timeout(8000),
    });
    if (!identity.ok) return fail();

    const id = opaqueId();
    const maxAge = Math.min(expires_in - 5, maxSessionSeconds);
    store.sessions.delete(request.cookies.get(sessionCookie)?.value ?? "");
    if (!remember(store.sessions, id, { token: access_token, expires: Date.now() + maxAge * 1000 })) return fail();
    const response = redirect(login.returnTo);
    setCookie(response, sessionCookie, id, maxAge);
    setCookie(response, flowCookie, "", 0);
    return response;
  } catch {
    return fail();
  }
}

export function sessionToken(request: NextRequest) {
  return readEntry(store.sessions, request.cookies.get(sessionCookie)?.value ?? "")?.token;
}

export function clearSession(request: NextRequest, response: NextResponse) {
  store.sessions.delete(request.cookies.get(sessionCookie)?.value ?? "");
  setCookie(response, sessionCookie, "", 0);
  return response;
}

export function logout(request: NextRequest) {
  return clearSession(request, redirect(connectedPage));
}
