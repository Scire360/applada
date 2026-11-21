export type MatchSummary = {
  id: string;
  title: string;
  startsAt: string;
  durationMinutes: number;
  status: "scheduled" | "cancelled";
  confirmedCount: number;
  capacity: number;
  myStatus: RegistrationStatus | null;
};

type RegistrationStatus = "confirmed" | "waitlisted" | "withdrawn";

export type MatchDetails = Omit<MatchSummary, "myStatus"> & {
  organizerId: string;
  timeZone: string;
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
  groupId?: string;
  venueId?: string;
};

export type GroupRole = "owner" | "admin" | "member";
export type GroupSummary = {
  id: string;
  name: string;
  description: string | null;
  memberCount: number;
  myRole: GroupRole;
  createdAt: string;
};
export type GroupDetails = GroupSummary;
export type GroupMember = { userId: string; displayName: string | null; role: GroupRole; joinedAt: string };
export type CreatedInvitation = { id: string; token: string; expiresAt: string; maxUses: number };

export function matchDate(startsAt: string, timeZone?: string) {
  return new Intl.DateTimeFormat("pt-BR", { dateStyle: "medium", timeStyle: "short", timeZone }).format(new Date(startsAt));
}

export function registrationLabel(status: RegistrationStatus, position?: number | null) {
  if (status === "confirmed") return "Confirmado";
  if (status === "waitlisted") return position ? `${position}º na lista de espera` : "Lista de espera";
  return "Inscrição retirada";
}
