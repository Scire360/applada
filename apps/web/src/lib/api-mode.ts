import "server-only";

import { backendApiConfigured } from "./backend-api";
import { localIntegrationConfigured } from "./local-auth";

export type ApiMode = "local" | "backend" | "preview";

export function getApiMode(): ApiMode {
  if (localIntegrationConfigured()) return "local";
  if (process.env.NODE_ENV === "production" || backendApiConfigured()) return "backend";
  return "preview";
}
