import { ApiError } from "./api-client.ts";

type AttendanceMatch = { startsAt: string; durationMinutes: number; status: string };

export function attendanceClosesAt(match: AttendanceMatch): number {
  return Date.parse(match.startsAt) + (match.durationMinutes + 24 * 60) * 60_000;
}

export function canTakeAttendance(match: AttendanceMatch, now: Date): "not-open" | "open" | "closed" {
  if (match.status !== "scheduled") return "closed";
  const startsAt = Date.parse(match.startsAt);
  if (now.getTime() < startsAt) return "not-open";
  return now.getTime() < attendanceClosesAt(match) ? "open" : "closed";
}

export function accessDenied(error?: ApiError): boolean {
  return error?.status === 401 || error?.status === 403;
}

export function attendanceLinkAction(phase: ReturnType<typeof canTakeAttendance>, isOrganizer: boolean | undefined, failure?: unknown) {
  if (phase !== "open" || isOrganizer === false) return "hidden";
  if (isOrganizer) return "link";
  if (failure === undefined) return "load";
  if (failure instanceof ApiError && (failure.status === 0 || (failure.status >= 500 && failure.status < 600))) return "retry";
  return "hidden";
}

export function isAttendanceRecheck(matchId: string, attempt: number, failure?: { matchId: string; attempt: number }): boolean {
  return failure?.matchId === matchId && failure.attempt !== attempt;
}

export function attendanceFocusTarget(buttonFocused: boolean, action: ReturnType<typeof attendanceLinkAction>) {
  if (buttonFocused && action === "link") return "link";
  if (buttonFocused && action === "hidden") return "row";
  return "none";
}

export function attendanceProblem(loadFailure?: ApiError, actionFailure?: ApiError): ApiError | undefined {
  return accessDenied(actionFailure) ? actionFailure : loadFailure ?? actionFailure;
}

export async function readAttendance<TPage, TMatch>(page: Promise<TPage>, match: Promise<TMatch>): Promise<[TPage, TMatch]> {
  try {
    return await Promise.all([page, match]);
  } catch (error) {
    if (error instanceof ApiError && accessDenied(error)) throw error;
    // Uma falha transitória pode chegar antes do 401/403 que exige ocultar a lista anterior.
    for (const result of await Promise.allSettled([page, match])) {
      if (result.status === "rejected" && result.reason instanceof ApiError && accessDenied(result.reason)) throw result.reason;
    }
    throw error;
  }
}

export function attendanceMessage(error: ApiError): string {
  if (error.status === 403) {
    return error.code === "not-organizer" ? "Só o organizador pode fazer a chamada." : "Você não tem permissão para fazer esta chamada.";
  }
  if (error.status === 409) {
    if (error.code === "attendance-not-open") return "A chamada abre no início da partida.";
    if (error.code === "attendance-closed") return "A chamada fechou.";
    if (error.code === "match-cancelled") return "A partida foi cancelada.";
    return "Não foi possível atualizar a chamada. Recarregue a página e tente novamente.";
  }
  if (error.status === 404 && error.code === "attendance-participant-not-found") {
    return "Este participante não tem inscrição confirmada nesta partida.";
  }
  return error.message;
}
