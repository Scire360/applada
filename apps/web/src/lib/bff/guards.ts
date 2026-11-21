// Guardas puras do BFF. Sem alias nem server-only: roda em node --test.
export const maxJsonBytes = 16 * 1024;

export function isSameOrigin(origin: string | null, publicOrigin: string): boolean {
  return origin !== null && origin === publicOrigin;
}

export function isJsonRequest(contentType: string | null): boolean {
  return contentType?.split(";")[0].trim().toLowerCase() === "application/json";
}

export function isAllowedResponseType(contentType: string | null): boolean {
  return contentType !== null && /^application\/(?:json|problem\+json)\s*(?:;|$)/i.test(contentType);
}

// Lê o corpo até maxBytes; acima disso cancela o stream e devolve null.
export async function readBoundedBody(body: ReadableStream<Uint8Array> | null, maxBytes: number): Promise<Uint8Array | null> {
  if (!body) return new Uint8Array();
  const reader = body.getReader();
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
