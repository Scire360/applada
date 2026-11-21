"use client";

import { useCallback, useSyncExternalStore } from "react";
import { attendanceClosesAt, canTakeAttendance } from "@/lib/attendance";
import type { MatchDetails } from "@/lib/connected-matches";

type Schedule = Pick<MatchDetails, "startsAt" | "status" | "durationMinutes">;
type Summary = Omit<Schedule, "durationMinutes">;

export function useAttendanceWindow(match?: Schedule | Summary) {
  const start = match ? Date.parse(match.startsAt) : NaN;
  const end = match && "durationMinutes" in match ? attendanceClosesAt(match) : NaN;
  const subscribe = useCallback((changed: () => void) => {
    let timer: number;
    function schedule() {
      const now = Date.now();
      const next = now < start ? start : end;
      if (next > now) timer = window.setTimeout(() => {
        changed();
        schedule();
      }, Math.min(next - now, 2_147_483_647));
    }
    schedule();
    return () => window.clearTimeout(timer);
  }, [start, end]);

  return useSyncExternalStore(subscribe, () => {
    if (!match || match.status !== "scheduled") return "closed";
    if ("durationMinutes" in match) return canTakeAttendance(match, new Date());
    return Date.now() < start ? "not-open" : "started";
  }, () => "not-open" as const);
}
