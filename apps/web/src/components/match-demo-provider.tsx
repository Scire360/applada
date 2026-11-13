"use client";

import { createContext, useContext, useState, type ReactNode } from "react";

export type MatchDraft = {
  title: string;
  date: string;
  time: string;
  location: string;
  capacity: number;
  notes: string;
};

export type MatchGoal = {
  id: number;
  player: string;
  side: "green" | "blue";
};

export type MatchStatus = "scheduled" | "live" | "completed";

type MatchDemoContextValue = {
  match: MatchDraft | null;
  status: MatchStatus;
  attendees: string[];
  waitlist: string[];
  checkedIn: string[];
  goals: MatchGoal[];
  playerOfTheMatch: string | null;
  createMatch: (draft: MatchDraft) => void;
  startMatch: () => boolean;
  finishMatch: () => boolean;
  toggleAttendance: (name: string) => AttendanceResult;
  toggleCheckIn: (name: string) => void;
  recordGoal: (player: string, side: MatchGoal["side"]) => boolean;
  removeGoal: (id: number) => void;
  voteForPlayer: (name: string) => boolean;
};

type AttendanceResult =
  | "confirmed"
  | "cancelled"
  | "promoted"
  | "waitlisted"
  | "left-waitlist"
  | "match-started"
  | "unavailable";

const MatchDemoContext = createContext<MatchDemoContextValue | null>(null);

export const demoPlayers = [
  "Rafael Martins",
  "Lucas Costa",
  "Bruno Alves",
  "João Pedro",
];

export function MatchDemoProvider({ children }: { children: ReactNode }) {
  const [match, setMatch] = useState<MatchDraft | null>(null);
  const [status, setStatus] = useState<MatchStatus>("scheduled");
  const [attendees, setAttendees] = useState<string[]>([]);
  const [waitlist, setWaitlist] = useState<string[]>([]);
  const [checkedIn, setCheckedIn] = useState<string[]>([]);
  const [goals, setGoals] = useState<MatchGoal[]>([]);
  const [playerOfTheMatch, setPlayerOfTheMatch] = useState<string | null>(null);
  const [nextGoalId, setNextGoalId] = useState(0);

  function createMatch(draft: MatchDraft) {
    setMatch(draft);
    setStatus("scheduled");
    setAttendees([]);
    setWaitlist([]);
    setCheckedIn([]);
    setGoals([]);
    setPlayerOfTheMatch(null);
    setNextGoalId(0);
  }

  function startMatch() {
    if (!match || status !== "scheduled" || checkedIn.length < 2) return false;
    setStatus("live");
    return true;
  }

  function finishMatch() {
    if (status !== "live") return false;
    setStatus("completed");
    return true;
  }

  function toggleAttendance(name: string) {
    if (status !== "scheduled") return "match-started";

    if (attendees.includes(name)) {
      const nextInLine = waitlist[0];
      setAttendees((current) => {
        const remaining = current.filter((attendee) => attendee !== name);
        return nextInLine ? [...remaining, nextInLine] : remaining;
      });
      if (nextInLine) setWaitlist((current) => current.slice(1));
      setCheckedIn((current) => current.filter((attendee) => attendee !== name));
      setGoals((current) => current.filter((goal) => goal.player !== name));
      setPlayerOfTheMatch((current) => current === name ? null : current);
      return nextInLine ? "promoted" : "cancelled";
    }

    if (waitlist.includes(name)) {
      setWaitlist((current) => current.filter((player) => player !== name));
      return "left-waitlist";
    }

    if (!match) return "unavailable";
    if (attendees.length >= match.capacity) {
      setWaitlist((current) => [...current, name]);
      return "waitlisted";
    }

    setAttendees((current) => [...current, name]);
    return "confirmed";
  }

  function toggleCheckIn(name: string) {
    if (status !== "scheduled") return;
    if (!attendees.includes(name)) return;
    setCheckedIn((current) => current.includes(name)
      ? current.filter((attendee) => attendee !== name)
      : [...current, name]);
  }

  function recordGoal(player: string, side: MatchGoal["side"]) {
    if (status !== "live" || !checkedIn.includes(player)) return false;
    setGoals((current) => [...current, { id: nextGoalId, player, side }]);
    setNextGoalId((current) => current + 1);
    return true;
  }

  function removeGoal(id: number) {
    if (status !== "live") return;
    setGoals((current) => current.filter((goal) => goal.id !== id));
  }

  function voteForPlayer(name: string) {
    if (status !== "completed" || !checkedIn.includes(name)) return false;
    setPlayerOfTheMatch(name);
    return true;
  }

  return (
    <MatchDemoContext.Provider
      value={{
        match,
        status,
        attendees,
        waitlist,
        checkedIn,
        goals,
        playerOfTheMatch,
        createMatch,
        startMatch,
        finishMatch,
        toggleAttendance,
        toggleCheckIn,
        recordGoal,
        removeGoal,
        voteForPlayer,
      }}
    >
      {children}
    </MatchDemoContext.Provider>
  );
}

export function useMatchDemo() {
  const context = useContext(MatchDemoContext);
  if (!context) throw new Error("useMatchDemo deve ser usado dentro de MatchDemoProvider.");
  return context;
}
