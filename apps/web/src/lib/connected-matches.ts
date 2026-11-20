export const localAccountHeader = "X-Applada-Account";

export type MatchSummary = {
  id: string;
  title: string;
  startsAt: string;
  status: "scheduled" | "cancelled";
  confirmedCount: number;
  capacity: number;
  myStatus: RegistrationStatus | null;
};

type RegistrationStatus = "confirmed" | "waitlisted" | "withdrawn";

export type MatchDetails = Omit<MatchSummary, "myStatus"> & {
  organizerId: string;
  timeZone: string;
  durationMinutes: number;
  waitlistCount: number;
  locationText: string | null;
  version: number;
  myRegistration: { status: RegistrationStatus; position: number | null } | null;
};

export type Participant = { userId: string; status: RegistrationStatus; position: number | null; displayName: string | null };
export type Page<T> = { items: T[]; nextCursor: string | null };
export type MatchRole = "organizer" | "participant";
export type MatchEdit = { title: string; locationText: string; capacity: number };
export type MatchCreation = MatchEdit & {
  startsAt: string;
  timeZone: string;
  durationMinutes: number;
  visibility: "public";
};

export type ApiCall = <T>(path: string, options?: RequestInit) => Promise<T>;

export class ApiError extends Error {
  constructor(readonly status: number, message: string) {
    super(message);
  }
}

async function callApi<T>(basePath: string, path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`${basePath}/${path}`, { ...options, cache: "no-store" });
  if (!response.ok) {
    const problem: { title?: string; errors?: Record<string, string[]> } = await response.json().catch(() => ({}));
    const detail = problem.errors && Object.values(problem.errors).flat().join(" ");
    const retryAfter = response.headers.get("Retry-After");
    const message = response.status === 412
      ? "A partida mudou. Atualize os dados e revise a edição."
      : response.status === 429 && retryAfter
        ? `Aguarde ${retryAfter} s antes de tentar novamente.`
        : detail || problem.title || "A operação falhou. Tente novamente.";
    throw new ApiError(response.status, message);
  }
  return response.status === 204 ? undefined as T : response.json();
}

export function localApi<T>(path: string, options?: RequestInit) {
  return callApi<T>("/api/local", path, options);
}

export function backendApi<T>(path: string, options?: RequestInit) {
  return callApi<T>("/api/backend", path, options);
}

export function matchDate(startsAt: string, timeZone?: string) {
  return new Intl.DateTimeFormat("pt-BR", { dateStyle: "medium", timeStyle: "short", timeZone }).format(new Date(startsAt));
}

export function registrationLabel(status: RegistrationStatus, position?: number | null) {
  if (status === "confirmed") return "Confirmado";
  if (status === "waitlisted") return position ? `${position}º na lista de espera` : "Lista de espera";
  return "Inscrição retirada";
}
