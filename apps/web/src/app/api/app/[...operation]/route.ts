import { NextResponse, type NextRequest } from "next/server";
import { accountHeader } from "@/lib/api-client";
import { handleAvatar } from "@/lib/avatar-storage";
import { isAllowedResponseType, isJsonRequest, isSameOrigin, maxJsonBytes, readBoundedBody } from "@/lib/bff/guards";
import { shouldClearInvitationCookie } from "@/lib/bff/invitation";
import { problem, secureResponse } from "@/lib/bff/http";
import { operationQuery, resolveOperation, type Operation } from "@/lib/bff/routes";
import { apiOrigin, getAccessToken, publicOrigin } from "@/lib/bff/session";

type Context = { params: Promise<{ operation: string[] }> };

const invitationCookie = "applada-invitation";
const invitationToken = /^[A-Za-z0-9_-]{43}$/;
const headerValue = /^[\x21-\x7e]{1,200}$/;
const entityTag = /^(?:W\/)?"[\x21\x23-\x7e]{0,64}"$/;

function invitationCookieOptions(maxAge: number) {
  return {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "lax" as const,
    path: "/api/app",
    maxAge,
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function parseJson(bytes: Uint8Array): unknown {
  try {
    return JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes));
  } catch {
    return undefined;
  }
}

async function readJsonBody(request: NextRequest): Promise<Uint8Array | NextResponse> {
  if (!isJsonRequest(request.headers.get("content-type"))) return problem(415, "Envie os dados em JSON.");
  const body = await readBoundedBody(request.body, maxJsonBytes);
  return body ?? problem(413, "Os dados enviados podem ter até 16 KB.");
}

// O navegador manda o token do convite antes do login; ele fica num cookie HttpOnly por 10 min.
async function savePendingInvitation(request: NextRequest) {
  const body = await readJsonBody(request);
  if (body instanceof NextResponse) return body;
  const data = parseJson(body);
  const token = isRecord(data) ? data.token : undefined;
  if (typeof token !== "string" || !invitationToken.test(token)) return problem(400, "Convite inválido ou expirado.");
  const response = secureResponse(new NextResponse(null, { status: 204 }));
  response.cookies.set(invitationCookie, token, invitationCookieOptions(600));
  return response;
}

// Conta da aba diferente da sessão atual: recusa gravar o perfil na conta errada.
async function accountChanged(request: NextRequest, accessToken: string, origin: string) {
  const expected = request.headers.get(accountHeader);
  if (expected === null) return false;
  const me = await fetch(new URL("/api/v1/me", origin), {
    headers: { Authorization: `Bearer ${accessToken}`, Accept: "application/json" },
    cache: "no-store",
    redirect: "error",
    signal: AbortSignal.timeout(8000),
  });
  if (!me.ok) {
    await me.body?.cancel();
    return me.status === 401 ? "expired" : "unavailable";
  }
  const { userId } = await me.json() as { userId?: unknown };
  return typeof userId !== "string" || userId.toLowerCase() !== expected.toLowerCase();
}

async function forward(request: NextRequest, operation: Operation, accessToken: string) {
  const origin = apiOrigin();
  const upstream = new URL(`/api/v1${operation.path}`, origin);
  upstream.search = operationQuery(operation, request.nextUrl.searchParams, request.headers).toString();

  const headers = new Headers({ Accept: "application/json, application/problem+json", Authorization: `Bearer ${accessToken}` });
  let body: Uint8Array<ArrayBuffer> | undefined;
  let consumedInvitation = false;
  if (operation.body === "json") {
    const read = await readJsonBody(request);
    if (read instanceof NextResponse) return read;
    body = new Uint8Array(read);
    headers.set("Content-Type", "application/json");

    // Aceite sem token no corpo: usa o convite guardado antes do login e o apaga.
    if (operation.path === "/group-invitations/acceptance") {
      const pending = request.cookies.get(invitationCookie)?.value;
      if (pending !== undefined) {
        consumedInvitation = true;
        const data = parseJson(read);
        if (isRecord(data) && !("token" in data) && invitationToken.test(pending)) {
          body = new TextEncoder().encode(JSON.stringify({ ...data, token: pending }));
        }
      }
    }
  }
  for (const name of operation.forward) {
    const value = request.headers.get(name);
    if (value !== null && headerValue.test(value)) headers.set(name, value);
  }

  if (operation.path === "/me/profile" && request.method !== "GET") {
    const changed = await accountChanged(request, accessToken, origin);
    if (changed === "expired") return problem(401, "Sua sessão expirou. Entre novamente.");
    if (changed === "unavailable") return problem(503, "A API do Applada está indisponível. Tente novamente em instantes.");
    if (changed) return problem(409, "A conta nesta aba mudou. Atualize a página e tente novamente.");
  }

  const backend = await fetch(upstream, {
    method: request.method,
    body,
    headers,
    cache: "no-store",
    redirect: "error",
    signal: AbortSignal.timeout(8000),
  });

  let response: NextResponse;
  if (backend.status === 401) {
    await backend.body?.cancel();
    response = problem(401, "Sua sessão expirou. Entre novamente.");
  } else {
    const contentType = backend.headers.get("content-type");
    const responseHeaders = new Headers();
    if (isAllowedResponseType(contentType)) {
      responseHeaders.set("Content-Type", contentType!);
    } else if (backend.status !== 204) {
      await backend.body?.cancel();
      return problem(502, "A API retornou uma resposta inesperada.");
    }
    const retryAfter = backend.headers.get("retry-after");
    if (retryAfter && /^\d{1,8}$/.test(retryAfter)) responseHeaders.set("Retry-After", retryAfter);
    const etag = backend.headers.get("etag");
    if (etag && entityTag.test(etag)) responseHeaders.set("ETag", etag);
    response = secureResponse(new NextResponse(backend.status === 204 ? null : backend.body, { status: backend.status, headers: responseHeaders }));
  }
  if (consumedInvitation && shouldClearInvitationCookie(backend.status)) response.cookies.set(invitationCookie, "", invitationCookieOptions(0));
  return response;
}

// Etapa em andamento, para o log dizer onde falhou sem expor URL, token, corpo ou mensagem.
type Stage = "config" | "body" | "upstream";

async function handle(request: NextRequest, { params }: Context) {
  let stage: Stage = "config";
  try {
    if (request.method !== "GET" && !isSameOrigin(request.headers.get("origin"), publicOrigin())) {
      return problem(403, "Esta operação precisa ser iniciada na página do Applada.");
    }

    const { operation } = await params;
    if (operation.length === 2 && operation[0] === "invitation" && operation[1] === "pending") {
      return request.method === "POST" ? await savePendingInvitation(request) : problem(404, "Operação não encontrada.");
    }

    const accessToken = await getAccessToken(request);
    if (!accessToken) return problem(401, "Entre para continuar.");

    if (operation.length === 1 && operation[0] === "avatar") {
      if (request.method !== "GET" && request.method !== "PUT" && request.method !== "DELETE") return problem(404, "Operação não encontrada.");
      return await handleAvatar(request, accessToken, request.headers.get(accountHeader));
    }

    const resolved = resolveOperation(operation, request.method);
    if (!resolved) return problem(404, "Operação não encontrada.");
    stage = "upstream";
    return await forward(request, resolved, accessToken);
  } catch (failure) {
    // Só a etapa e o nome do erro vão para o log: nada de token, corpo, URL ou mensagem.
    console.error("BFF /api/app: falha.", { stage, error: failure instanceof Error ? failure.name : typeof failure });
    return problem(503, "A API do Applada está indisponível. Tente novamente em instantes.");
  }
}

export { handle as GET, handle as POST, handle as PATCH, handle as PUT, handle as DELETE };
