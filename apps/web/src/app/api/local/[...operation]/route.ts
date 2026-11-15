import { NextRequest, NextResponse } from "next/server";
import { handleAvatar } from "@/lib/avatar-storage";
import { localAccountHeader } from "@/lib/connected-matches";
import { apiOrigin, beginLogin, clearSession, completeLogin, currentSession, localError, localIntegrationEnabled, logout, matchIdPattern, secureResponse, webOrigin } from "@/lib/local-auth";

type Context = { params: Promise<{ operation: string[] }> };

function apiPath(operation: string[], method: string) {
  const [name, id, action, member] = operation;
  if (name === "session" && operation.length === 1 && method === "GET") return "/me";
  if (name === "profile" && operation.length === 1 && (method === "GET" || method === "PUT")) return "/me/profile";
  if (name === "mine" && operation.length === 1 && method === "GET") return "/me/matches";
  if (name !== "matches") return null;
  if (operation.length === 1 && method === "POST") return "/matches";
  if (!id || !matchIdPattern.test(id)) return null;
  const match = `/matches/${id}`;
  if (operation.length === 2 && (method === "GET" || method === "PATCH")) return match;
  if (operation.length === 3 && action === "cancellation" && method === "POST") return match + "/cancellation";
  if (action !== "registrations") return null;
  if (operation.length === 3 && (method === "GET" || method === "POST")) return match + "/registrations";
  if (operation.length === 4 && member === "me" && method === "DELETE") return match + "/registrations/me";
  return null;
}

async function readBoundedBody(request: NextRequest, maxBytes = 16 * 1024) {
  if (!request.body) return new Uint8Array();
  const reader = request.body.getReader();
  const buffer = new Uint8Array(maxBytes);
  let length = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) return buffer.slice(0, length);
      if (length + value.length > maxBytes) {
        await reader.cancel();
        return null;
      }
      buffer.set(value, length);
      length += value.length;
    }
  } finally {
    reader.releaseLock();
  }
}

async function handle(request: NextRequest, { params }: Context) {
  if (!localIntegrationEnabled(request)) return localError(404, "Integração local desativada.");
  if (request.method !== "GET" && request.headers.get("origin") !== webOrigin) {
    return localError(403, "Esta operação precisa ser iniciada na página do Applada.");
  }

  const { operation } = await params;
  if (operation.length === 1) {
    if (operation[0] === "login" && request.method === "POST") return beginLogin(request);
    if (operation[0] === "callback" && request.method === "GET") return completeLogin(request);
    if (operation[0] === "logout" && request.method === "POST") return logout(request);
  }

  const avatar = operation.length === 1 && operation[0] === "avatar"
    && (request.method === "GET" || request.method === "PUT" || request.method === "DELETE");
  const path = apiPath(operation, request.method);
  if (!path && !avatar) return localError(404, "Operação não encontrada.");
  const session = currentSession(request);
  if (!session) return localError(401, "Entre com uma conta de teste para continuar.");

  const expectedAccount = request.headers.get(localAccountHeader);
  if ((avatar || path === "/me/profile") && expectedAccount !== null && expectedAccount !== session.userId) {
    return localError(409, "A conta nesta aba mudou. Atualize a página e tente novamente.");
  }
  if (!path) return handleAvatar(request, session.token, readBoundedBody);

  const upstream = new URL(`/api/v1${path}`, apiOrigin);
  if (request.method === "GET" && (path === "/me/matches" || path.endsWith("/registrations"))) {
    const keys = path === "/me/matches" ? ["role", "cursor", "limit"] : ["cursor", "limit"];
    for (const key of keys) {
      const value = request.nextUrl.searchParams.get(key);
      if (value !== null) upstream.searchParams.set(key, value);
    }
  }

  const headers = new Headers({ Accept: "application/json, application/problem+json", Authorization: `Bearer ${session.token}` });
  try {
    let body: Uint8Array<ArrayBuffer> | undefined;
    if (path === "/matches" || request.method === "PATCH" || request.method === "PUT") {
      const profile = path === "/me/profile";
      const type = request.headers.get("content-type")?.split(";")[0].trim().toLowerCase();
      if (type !== "application/json") return localError(415, profile ? "Envie os dados do perfil em JSON." : "Envie os dados da partida em JSON.");
      const bounded = await readBoundedBody(request);
      if (!bounded) return localError(413, profile ? "Os dados do perfil podem ter até 16 KB." : "Os dados da partida podem ter até 16 KB.");
      body = bounded;
      headers.set("Content-Type", "application/json");
      const key = request.method === "PATCH" ? "If-Match" : path === "/matches" ? "Idempotency-Key" : null;
      if (key) {
        const value = request.headers.get(key);
        if (value) headers.set(key, value);
      }
    }
    const backend = await fetch(upstream, {
      method: request.method,
      body,
      headers,
      cache: "no-store",
      redirect: "error",
      signal: AbortSignal.timeout(8000),
    });
    if (backend.status === 401) return clearSession(request, localError(401, "A sessão expirou. Entre novamente."));
    const responseHeaders = new Headers();
    const contentType = backend.headers.get("content-type");
    if (contentType && /^application\/(?:json|problem\+json)(?:;|$)/i.test(contentType)) {
      responseHeaders.set("Content-Type", contentType);
    } else if (backend.status !== 204) {
      return localError(502, "A API retornou uma resposta inesperada.");
    }
    const retryAfter = backend.headers.get("retry-after");
    if (retryAfter && /^\d{1,8}$/.test(retryAfter)) responseHeaders.set("Retry-After", retryAfter);
    return secureResponse(new NextResponse(backend.body, { status: backend.status, headers: responseHeaders }));
  } catch {
    return localError(503, "A API do Applada está indisponível. Tente novamente em instantes.");
  }
}

export { handle as GET, handle as POST, handle as PATCH, handle as PUT, handle as DELETE };
