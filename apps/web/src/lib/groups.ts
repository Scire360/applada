// Regras puras das telas de grupos. Sem alias de import: roda em node --test.
export type Role = "owner" | "admin" | "member";

const tokenPattern = /^[A-Za-z0-9_-]{43}$/;

// Lê o token do convite em location.hash ("#token"); null se ausente ou malformado.
export function invitationFromHash(hash: string): string | null {
  const value = hash.startsWith("#") ? hash.slice(1) : "";
  return tokenPattern.test(value) ? value : null;
}

// O que quem vê (myRole) pode fazer com outro membro. Quem sai do grupo usa "Sair do grupo".
export function memberActions(myRole: Role, myUserId: string, target: { userId: string; role: Role }): { remove: boolean; role: "admin" | "member" | null } {
  if (target.userId === myUserId || target.role === "owner") return { remove: false, role: null };
  if (myRole === "owner") return { remove: true, role: target.role === "member" ? "admin" : "member" };
  if (myRole === "admin") return { remove: target.role === "member", role: null };
  return { remove: false, role: null };
}

export function canLeave(myRole: Role) {
  return myRole !== "owner";
}

export function canManageInvitations(myRole: Role) {
  return myRole === "owner" || myRole === "admin";
}

// O efeito pode rodar duas vezes (Strict Mode em dev) e a 1ª leitura já apagou o hash:
// um token lido antes nunca é sobrescrito por uma leitura vazia.
export function keepInvitation(previous: string | null, hash: string): string | null {
  return previous ?? invitationFromHash(hash);
}
