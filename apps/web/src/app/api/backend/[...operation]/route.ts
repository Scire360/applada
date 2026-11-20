import { getToken } from "next-auth/jwt";
import { NextRequest, NextResponse } from "next/server";
import { auth, getPublicAppUrl } from "@/auth";
import { backendApiOrigin } from "@/lib/backend-api";
import { mapApiPath } from "@/lib/api-routes";

type Context = { params: Promise<{ operation: string[] }> };
type BoundedBodyResult =
  | { kind: "ok"; body: Uint8Array<ArrayBuffer> }
  | { kind: "too-large" }
  | { kind: "read-failed" };

function problem(status: number, title: string) {
  return NextResponse.json({ status, title }, {
    status,
    headers: {
      "Cache-Control": "private, no-store",
      "Content-Type": "application/problem+json",
      "Referrer-Policy": "no-referrer",
      "X-Content-Type-Options": "nosniff",
    },
  });
}

function isSameOrigin(request: NextRequest) {
  return request.headers.get("origin") === getPublicAppUrl();
}

async function readBoundedBody(request: NextRequest, maxBytes = 16 * 1024): Promise<BoundedBodyResult> {
  if (!request.body) return { kind: "ok", body: new Uint8Array() };
  const reader = request.body.getReader();
  const buffer = new Uint8Array(maxBytes);
  let length = 0;
  let tooLarge = false;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) return { kind: "ok", body: buffer.slice(0, length) };
      if (length + value.length > maxBytes) {
        tooLarge = true;
        await reader.cancel();
        return { kind: "too-large" };
      }
      buffer.set(value, length);
      length += value.length;
    }
  } catch {
    return tooLarge ? { kind: "too-large" } : { kind: "read-failed" };
  } finally {
    reader.releaseLock();
  }
}

async function handle(request: NextRequest, { params }: Context) {
  if (request.method !== "GET" && !isSameOrigin(request)) {
    return problem(403, "Esta operação precisa ser iniciada na página do Applada.");
  }

  const { operation } = await params;
  const path = mapApiPath(operation, request.method);
  if (!path) return problem(404, "Operação não encontrada.");

  let session;
  try {
    session = await auth();
  } catch {
    return problem(503, "Não foi possível validar sua sessão agora.");
  }
  if (!session?.user) return problem(401, "Entre novamente para continuar.");

  const secret = process.env.AUTH_SECRET;
  if (!secret) return problem(503, "A autenticação do servidor não está configurada.");

  let token;
  try {
    const cookieHeaders = new Headers(request.headers);
    cookieHeaders.delete("authorization");
    token = await getToken({
      req: { headers: cookieHeaders },
      secret,
      secureCookie: process.env.NODE_ENV === "production",
    });
  } catch {
    return problem(401, "Entre novamente para continuar.");
  }
  if (typeof token?.keycloakAccessToken !== "string" || token.keycloakAccessToken.length > 8192) {
    return problem(401, "Entre novamente para continuar.");
  }

  let upstream: URL;
  try {
    upstream = new URL(`/api/v1${path}`, backendApiOrigin());
  } catch {
    return problem(503, "A API do Applada não está configurada corretamente.");
  }

  if (request.method === "GET" && (path === "/me/matches" || path.endsWith("/registrations"))) {
    const keys = path === "/me/matches" ? ["role", "cursor", "limit"] : ["cursor", "limit"];
    for (const key of keys) {
      const value = request.nextUrl.searchParams.get(key);
      if (value !== null) upstream.searchParams.set(key, value);
    }
  }

  const headers = new Headers({
    Accept: "application/json, application/problem+json",
    Authorization: `Bearer ${token.keycloakAccessToken}`,
  });
  let body: Uint8Array<ArrayBuffer> | undefined;
  if (path === "/matches" || request.method === "PATCH" || request.method === "PUT") {
    const isProfile = path === "/me/profile";
    const contentType = request.headers.get("content-type")?.split(";")[0].trim().toLowerCase();
    if (contentType !== "application/json") {
      return problem(415, isProfile ? "Envie os dados do perfil em JSON." : "Envie os dados da partida em JSON.");
    }
    const bounded = await readBoundedBody(request);
    if (bounded.kind === "too-large") {
      return problem(413, isProfile ? "Os dados do perfil podem ter até 16 KB." : "Os dados da partida podem ter até 16 KB.");
    }
    if (bounded.kind === "read-failed") {
      return problem(400, "Não foi possível ler os dados enviados.");
    }
    body = bounded.body;
    headers.set("Content-Type", "application/json");
    const forwardedHeader = request.method === "PATCH" ? "If-Match" : path === "/matches" ? "Idempotency-Key" : null;
    const value = forwardedHeader ? request.headers.get(forwardedHeader) : null;
    if (forwardedHeader && value) headers.set(forwardedHeader, value);
  }

  try {
    const backend = await fetch(upstream, {
      method: request.method,
      body,
      headers,
      cache: "no-store",
      redirect: "error",
      signal: AbortSignal.timeout(8000),
    });
    const responseHeaders = new Headers();
    const contentType = backend.headers.get("content-type");
    if (contentType && /^application\/(?:json|problem\+json)(?:;|$)/i.test(contentType)) {
      responseHeaders.set("Content-Type", contentType);
    } else if (backend.status !== 204) {
      await backend.body?.cancel();
      return problem(502, "A API retornou uma resposta inesperada.");
    }
    const retryAfter = backend.headers.get("retry-after");
    if (retryAfter && /^\d{1,8}$/.test(retryAfter)) responseHeaders.set("Retry-After", retryAfter);
    responseHeaders.set("Cache-Control", "private, no-store");
    responseHeaders.set("Referrer-Policy", "no-referrer");
    responseHeaders.set("X-Content-Type-Options", "nosniff");
    return new NextResponse(backend.body, { status: backend.status, headers: responseHeaders });
  } catch {
    return problem(503, "A API do Applada está indisponível. Tente novamente em instantes.");
  }
}

export { handle as GET, handle as POST, handle as PATCH, handle as PUT, handle as DELETE };
