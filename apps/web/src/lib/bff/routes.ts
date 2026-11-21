// Tabela de operações que o BFF repassa para a API (/api/v1). Sem alias nem server-only: roda em node --test.
import { guidPattern } from "../guid.ts";

export type Operation = {
  path: string;
  query: readonly string[];
  body: "json" | "none";
  forward: readonly ("Idempotency-Key" | "If-Match")[];
};

const paging = ["cursor", "limit"] as const;

type Rule = {
  // "{}" marca um segmento que precisa ser GUID; o resto é literal.
  pattern: readonly string[];
  // Caminho na API quando difere dos segmentos (profile -> /me/profile).
  api?: string;
  methods: Readonly<Record<string, Pick<Operation, "query" | "body" | "forward"> | undefined>>;
};

const none = { query: [], body: "none", forward: [] } as const;
const json = { query: [], body: "json", forward: [] } as const;
const page = { query: paging, body: "none", forward: [] } as const;

const rules: readonly Rule[] = [
  { pattern: ["me"], methods: { GET: none } },
  { pattern: ["profile"], api: "/me/profile", methods: { GET: none, PUT: json } },
  { pattern: ["me", "matches"], methods: { GET: { query: ["role", ...paging], body: "none", forward: [] } } },
  { pattern: ["matches"], methods: { POST: { query: [], body: "json", forward: ["Idempotency-Key"] } } },
  { pattern: ["matches", "{}"], methods: { GET: none, PATCH: { query: [], body: "json", forward: ["If-Match"] } } },
  { pattern: ["matches", "{}", "cancellation"], methods: { POST: none } },
  { pattern: ["matches", "{}", "registrations"], methods: { GET: page, POST: none } },
  { pattern: ["matches", "{}", "registrations", "me"], methods: { DELETE: none } },
  { pattern: ["matches", "{}", "attendance"], methods: { GET: page } },
  { pattern: ["matches", "{}", "attendance", "{}"], methods: { PUT: json } },
  { pattern: ["me", "groups"], methods: { GET: page } },
  { pattern: ["groups"], methods: { POST: json } },
  { pattern: ["groups", "{}"], methods: { GET: none } },
  { pattern: ["groups", "{}", "members"], methods: { GET: page } },
  { pattern: ["groups", "{}", "members", "{}"], methods: { PATCH: json, DELETE: none } },
  { pattern: ["groups", "{}", "invitations"], methods: { POST: json } },
  { pattern: ["groups", "{}", "invitations", "{}"], methods: { DELETE: none } },
  { pattern: ["groups", "{}", "matches"], methods: { GET: page } },
  { pattern: ["group-invitations", "acceptance"], methods: { POST: json } },
];

export function resolveOperation(segments: readonly string[], method: string): Operation | null {
  for (const rule of rules) {
    if (rule.pattern.length !== segments.length) continue;
    const matches = rule.pattern.every((part, index) => (part === "{}" ? guidPattern.test(segments[index]) : part === segments[index]));
    if (!matches) continue;
    const config = Object.hasOwn(rule.methods, method) ? rule.methods[method] : undefined;
    return config ? { path: rule.api ?? `/${segments.join("/")}`, ...config } : null;
  }
  return null;
}
