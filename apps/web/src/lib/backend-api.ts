import "server-only";

export function backendApiConfigured() {
  return Boolean(process.env.APPLADA_API_ORIGIN?.trim());
}

export function backendApiOrigin() {
  const configuredOrigin = process.env.APPLADA_API_ORIGIN?.trim();
  if (!configuredOrigin) throw new Error("APPLADA_API_ORIGIN is not configured.");

  const url = new URL(configuredOrigin);
  const loopback = ["localhost", "127.0.0.1", "[::1]", "::1"].includes(url.hostname);
  const allowedProtocol = url.protocol === "https:"
    || (process.env.NODE_ENV === "development" && loopback && url.protocol === "http:");
  if (!allowedProtocol || url.username || url.password || url.pathname !== "/" || url.search || url.hash) {
    throw new Error("APPLADA_API_ORIGIN must be an HTTPS origin; HTTP is allowed only for a local development host.");
  }

  return url.origin;
}
