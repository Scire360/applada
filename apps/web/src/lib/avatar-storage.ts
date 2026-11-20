import "server-only";

import { Buffer } from "node:buffer";
import { deflateSync } from "node:zlib";
import { NextResponse, type NextRequest } from "next/server";
import { apiOrigin, localError, matchIdPattern, secureResponse } from "./local-auth";
import { avatarPixelBytes, avatarSide } from "./avatar-photo";

const bucket = "applada-avatars";
const pngSignature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);

function pngChunk(type: string, payload: Buffer) {
  const content = Buffer.concat([Buffer.from(type, "ascii"), payload]);
  let checksum = 0xffffffff;
  for (const value of content) {
    checksum ^= value;
    for (let bit = 0; bit < 8; bit++) {
      checksum = (checksum >>> 1) ^ (checksum & 1 ? 0xedb88320 : 0);
    }
  }

  const size = Buffer.alloc(4);
  size.writeUInt32BE(payload.length);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE((checksum ^ 0xffffffff) >>> 0);
  return Buffer.concat([size, content, crc]);
}

function encodeAvatar(pixels: Uint8Array) {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(avatarSide, 0);
  header.writeUInt32BE(avatarSide, 4);
  header[8] = 8;
  header[9] = 6; // RGBA de 8 bits por canal

  const rowBytes = avatarSide * 4;
  const scanlines = Buffer.alloc((rowBytes + 1) * avatarSide);
  for (let row = 0; row < avatarSide; row++) {
    // Cada linha começa com o byte zero do filtro PNG None.
    scanlines.set(pixels.subarray(row * rowBytes, (row + 1) * rowBytes), row * (rowBytes + 1) + 1);
  }
  return Buffer.concat([
    pngSignature,
    pngChunk("IHDR", header),
    pngChunk("IDAT", deflateSync(scanlines)),
    pngChunk("IEND", Buffer.alloc(0)),
  ]);
}

function getStorageConfig() {
  const url = process.env.APPLADA_SUPABASE_URL;
  const key = process.env.APPLADA_SUPABASE_STORAGE_KEY;
  if (!url || !key) return null;

  const base = new URL(url);
  const loopback = base.hostname === "localhost" || base.hostname === "127.0.0.1";
  if ((base.protocol !== "https:" && !(loopback && base.protocol === "http:")) || base.username || base.password) {
    return null;
  }

  const headers = new Headers({ apikey: key });
  // O CLI local ainda pode fornecer service_role no formato JWT legado.
  if (!key.startsWith("sb_secret_")) headers.set("Authorization", "Bearer " + key);
  return { origin: base.origin, headers };
}

type StorageConfig = NonNullable<ReturnType<typeof getStorageConfig>>;

function storageRequest(config: StorageConfig, path: string, init: RequestInit = {}) {
  const headers = new Headers(config.headers);
  new Headers(init.headers).forEach((value, name) => headers.set(name, value));
  return fetch(new URL("/storage/v1" + path, config.origin), {
    ...init, headers, cache: "no-store", redirect: "error", signal: AbortSignal.timeout(5000),
  });
}

function emptyAvatar() {
  return secureResponse(new NextResponse(null, { status: 204 }));
}

async function requireStorageSuccess(response: Response) {
  if (!response.ok) {
    await response.body?.cancel();
    throw new Error("O Storage não concluiu a operação.");
  }
}

async function readAvatar(config: StorageConfig, objectPath: string) {
  const response = await storageRequest(config, "/object/" + objectPath, {
    headers: { "Cache-Control": "no-cache" },
  });
  if (response.status === 404) {
    await response.body?.cancel();
    return emptyAvatar();
  }
  if (response.status === 400) {
    const problem = await response.json() as { statusCode?: string | number; code?: string };
    if (String(problem.statusCode) === "404" || problem.code === "NoSuchKey") return emptyAvatar();
    throw new Error("Falha ao ler o avatar do Storage.");
  }
  if (!response.ok || response.headers.get("content-type")?.split(";")[0] !== "image/png") {
    await response.body?.cancel();
    throw new Error("Falha ao ler o avatar do Storage.");
  }

  return secureResponse(new NextResponse(response.body, { headers: { "Content-Type": "image/png" } }));
}

async function putAvatar(config: StorageConfig, objectPath: string, pixels: Uint8Array) {
  const status = await storageRequest(config, "/bucket/" + bucket);
  await requireStorageSuccess(status);
  if ((await status.json() as { public: boolean }).public !== false) {
    throw new Error("O bucket de avatares precisa ser privado.");
  }

  const path = "/object/upload/sign/" + objectPath;
  const signed = await storageRequest(config, path, {
    method: "POST",
    headers: { "Content-Type": "application/json", "x-upsert": "true" },
    body: "{}",
  });
  await requireStorageSuccess(signed);
  const { url } = await signed.json() as { url: string };
  const ticket = new URL(url, config.origin).searchParams.get("token");
  if (!ticket) throw new Error("O Storage não devolveu a autorização.");

  // A URL assinada é usada imediatamente pelo servidor. O navegador recebe somente o resultado.
  const result = await storageRequest(config, path + "?token=" + encodeURIComponent(ticket), {
    method: "PUT",
    headers: { "Content-Type": "image/png", "Cache-Control": "max-age=0", "x-upsert": "true" },
    body: new Uint8Array(encodeAvatar(pixels)),
  });
  await requireStorageSuccess(result);
  await result.body?.cancel();
  return emptyAvatar();
}

export async function handleAvatar(
  request: NextRequest,
  accessToken: string,
  readBody: (request: NextRequest, limit: number) => Promise<Uint8Array<ArrayBuffer> | null>,
) {
  try {
    const session = await fetch(new URL("/api/v1/me", apiOrigin), {
      headers: { Authorization: "Bearer " + accessToken },
      cache: "no-store",
      redirect: "error",
      signal: AbortSignal.timeout(5000),
    });
    if (session.status === 401) {
      await session.body?.cancel();
      return localError(401, "Entre novamente para acessar sua foto.");
    }
    await requireStorageSuccess(session);
    const { userId } = await session.json() as { userId: string };
    if (!matchIdPattern.test(userId)) throw new Error("Identificador de usuário inválido.");

    const config = getStorageConfig();
    if (!config) return localError(503, "O armazenamento de fotos ainda não está configurado.");
    const filename = userId.toLowerCase() + ".png";
    const objectPath = bucket + "/" + filename;

    if (request.method === "GET") return await readAvatar(config, objectPath);
    if (request.method === "DELETE") {
      const removed = await storageRequest(config, "/object/" + bucket, {
        method: "DELETE",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ prefixes: [filename] }),
      });
      await requireStorageSuccess(removed);
      await removed.body?.cancel();
      return emptyAvatar();
    }

    if (request.headers.get("content-type") !== "application/octet-stream") {
      return localError(415, "Selecione a foto pelas configurações da sua conta.");
    }
    const pixels = await readBody(request, avatarPixelBytes);
    if (!pixels) return localError(413, "A prévia da foto excedeu o tamanho permitido.");
    if (pixels.length !== avatarPixelBytes) return localError(400, "A prévia da foto está incompleta.");
    return await putAvatar(config, objectPath, pixels);
  } catch {
    return localError(503, "O armazenamento de fotos está indisponível. Tente novamente.");
  }
}
