export const matchIdPattern = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

export function mapApiPath(operation: string[], method: string) {
  const [name, id, action, member] = operation;
  if (name === "session" && operation.length === 1 && method === "GET") return "/me";
  if (name === "profile" && operation.length === 1 && (method === "GET" || method === "PUT")) return "/me/profile";
  if (name === "mine" && operation.length === 1 && method === "GET") return "/me/matches";
  if (name !== "matches") return null;
  if (operation.length === 1 && method === "POST") return "/matches";
  if (!id || !matchIdPattern.test(id)) return null;
  const match = `/matches/${id}`;
  if (operation.length === 2 && (method === "GET" || method === "PATCH")) return match;
  if (operation.length === 3 && action === "cancellation" && method === "POST") return match + "/cancellation";
  if (action !== "registrations") return null;
  if (operation.length === 3 && (method === "GET" || method === "POST")) return match + "/registrations";
  if (operation.length === 4 && member === "me" && method === "DELETE") return match + "/registrations/me";
  return null;
}
