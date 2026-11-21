import type { ApiError } from "./api-client";

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

export function attendanceMessage(error: ApiError): string {
  if (error.status === 403 && error.code === "not-organizer") return "Só o organizador pode fazer a chamada.";
  if (error.status === 409) {
    if (error.code === "attendance-not-open") return "A chamada abre no início da partida.";
    if (error.code === "attendance-closed") return "A chamada fechou.";
    if (error.code === "match-cancelled") return "A partida foi cancelada.";
  }
  if (error.status === 404 && error.code === "attendance-participant-not-found") {
    return "Este participante não tem inscrição confirmada nesta partida.";
  }
  return error.message;
}
