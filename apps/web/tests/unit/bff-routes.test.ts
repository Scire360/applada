import assert from "node:assert/strict";
import { test } from "node:test";
import { operationQuery, resolveOperation } from "../../src/lib/bff/routes.ts";

const G = "123e4567-e89b-12d3-a456-426614174000";
const H = "223e4567-e89b-12d3-a456-426614174001";
const paging = ["cursor", "limit"];

test("só a busca traduz os cabeçalhos de posição para a query da API", () => {
  const search = resolveOperation(["discover", "matches"], "GET")!;
  const myVenues = resolveOperation(["me", "venues"], "GET")!;
  const query = new URLSearchParams("radiusKm=10&cursor=a%2Bb&outro=x");
  const headers = new Headers({ "X-Applada-Lat": "-20.47", "X-Applada-Lon": "-54.62" });
  assert.deepEqual(Object.fromEntries(operationQuery(search, query, headers)), {
    lat: "-20.47", lon: "-54.62", radiusKm: "10", cursor: "a+b",
  });
  assert.deepEqual(Object.fromEntries(operationQuery(myVenues, query, headers)), { cursor: "a+b" });
  assert.equal(operationQuery(search, new URLSearchParams("lat=1&lon=2"), new Headers()).toString(), "lat=1&lon=2");
});

test("descoberta: somente os caminhos, métodos e parâmetros do contrato", () => {
  const cases = [
    { segments: ["venues"], method: "POST", body: "json", query: [] },
    { segments: ["venues", G], method: "GET", body: "none", query: [] },
    { segments: ["me", "venues"], method: "GET", body: "none", query: paging },
    { segments: ["discover", "matches"], method: "GET", body: "none", query: ["lat", "lon", "radiusKm", ...paging] },
  ];
  for (const { segments, method, body, query } of cases) {
    assert.deepEqual(resolveOperation(segments, method), { path: "/" + segments.join("/"), body, query, forward: [] });
    for (const other of ["GET", "POST", "PUT", "PATCH", "DELETE"].filter((verb) => verb !== method)) {
      assert.equal(resolveOperation(segments, other), null);
    }
  }
  assert.equal(resolveOperation(["venues", "inválido"], "GET"), null);
});

test("chamada: lista por cursor e grava o status em JSON", () => {
  assert.deepEqual(resolveOperation(["matches", G, "attendance"], "GET"), {
    path: `/matches/${G}/attendance`, query: paging, body: "none", forward: [],
  });
  assert.deepEqual(resolveOperation(["matches", G, "attendance", H], "PUT"), {
    path: `/matches/${G}/attendance/${H}`, query: [], body: "json", forward: [],
  });
});

test("chamada: recusa métodos fora do contrato e IDs inválidos", () => {
  for (const method of ["POST", "PUT", "PATCH", "DELETE"]) {
    assert.equal(resolveOperation(["matches", G, "attendance"], method), null);
  }
  for (const method of ["GET", "POST", "PATCH", "DELETE"]) {
    assert.equal(resolveOperation(["matches", G, "attendance", H], method), null);
  }
  assert.equal(resolveOperation(["matches", "inválido", "attendance"], "GET"), null);
  assert.equal(resolveOperation(["matches", G, "attendance", "inválido"], "PUT"), null);
});

test("grupos: partidas do grupo paginadas", () => {
  assert.deepEqual(resolveOperation(["groups", G, "matches"], "GET"), {
    path: `/groups/${G}/matches`, query: paging, body: "none", forward: [],
  });
});

test("cabeçalhos repassados", () => {
  assert.deepEqual(resolveOperation(["matches"], "POST")?.forward, ["Idempotency-Key"]);
  assert.deepEqual(resolveOperation(["matches", G], "PATCH")?.forward, ["If-Match"]);
  assert.deepEqual(resolveOperation(["matches", G], "GET")?.forward, []);
});

test("me/matches aceita role, cursor e limit", () => {
  assert.deepEqual(resolveOperation(["me", "matches"], "GET")?.query, ["role", "cursor", "limit"]);
});

test("tabela completa", () => {
  const cases: [string[], string, string, "json" | "none"][] = [
    [["me"], "GET", "/me", "none"],
    [["profile"], "GET", "/me/profile", "none"],
    [["profile"], "PUT", "/me/profile", "json"],
    [["matches"], "POST", "/matches", "json"],
    [["matches", G], "GET", `/matches/${G}`, "none"],
    [["matches", G], "PATCH", `/matches/${G}`, "json"],
    [["matches", G, "cancellation"], "POST", `/matches/${G}/cancellation`, "none"],
    [["matches", G, "registrations"], "GET", `/matches/${G}/registrations`, "none"],
    [["matches", G, "registrations"], "POST", `/matches/${G}/registrations`, "none"],
    [["matches", G, "registrations", "me"], "DELETE", `/matches/${G}/registrations/me`, "none"],
    [["me", "groups"], "GET", "/me/groups", "none"],
    [["groups"], "POST", "/groups", "json"],
    [["groups", G], "GET", `/groups/${G}`, "none"],
    [["groups", G, "members"], "GET", `/groups/${G}/members`, "none"],
    [["groups", G, "members", H], "PATCH", `/groups/${G}/members/${H}`, "json"],
    [["groups", G, "members", H], "DELETE", `/groups/${G}/members/${H}`, "none"],
    [["groups", G, "invitations"], "POST", `/groups/${G}/invitations`, "json"],
    [["groups", G, "invitations", H], "DELETE", `/groups/${G}/invitations/${H}`, "none"],
    [["group-invitations", "acceptance"], "POST", "/group-invitations/acceptance", "json"],
  ];
  for (const [segments, method, path, body] of cases) {
    const op = resolveOperation(segments, method);
    assert.equal(op?.path, path, `${method} ${segments.join("/")}`);
    assert.equal(op?.body, body, `${method} ${segments.join("/")}`);
  }
});

test("listas paginadas expõem cursor e limit", () => {
  for (const s of [["me", "groups"], ["groups", G, "members"], ["matches", G, "registrations"]]) {
    assert.deepEqual(resolveOperation(s, "GET")?.query, paging);
  }
  assert.deepEqual(resolveOperation(["profile"], "GET")?.query, []);
});

test("recusas", () => {
  const refused: [string[], string][] = [
    [["groups", "..", "x"], "GET"],
    [["groups", "not-a-guid"], "GET"],
    [["matches", G], "DELETE"],
    [["admin"], "GET"],
    [["groups", G, "members", G], "GET"],
    [["groups", G, "matches", "extra"], "GET"],
    [["me"], "POST"],
    [["matches", G, "registrations", "me"], "GET"],
    [["groups", G, "members", "x"], "DELETE"],
    [[], "GET"],
    [["avatar"], "GET"],
    [["invitation", "pending"], "GET"],
    [["me"], "get"],
  ];
  for (const [segments, method] of refused) {
    assert.equal(resolveOperation(segments, method), null, `${method} ${segments.join("/")}`);
  }
});
