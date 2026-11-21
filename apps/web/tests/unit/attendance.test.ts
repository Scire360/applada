import assert from "node:assert/strict";
import { test } from "node:test";
import { attendanceMessage, canTakeAttendance } from "../../src/lib/attendance.ts";
import { ApiError, appApi } from "../../src/lib/api-client.ts";

const match = { startsAt: "2026-11-21T19:00:00-03:00", durationMinutes: 90, status: "scheduled" };

test("chamada inclui o início e exclui o fim da partida mais 24 horas", () => {
  for (const [instant, expected] of [
    ["2026-11-21T21:59:59.999Z", "not-open"],
    ["2026-11-21T22:00:00.000Z", "open"],
    ["2026-11-22T23:29:59.999Z", "open"],
    ["2026-11-22T23:30:00.000Z", "closed"],
    ["2026-11-23T00:00:00.000Z", "closed"],
  ]) {
    assert.equal(canTakeAttendance(match, new Date(instant)), expected, instant);
  }
});

test("partida cancelada tem chamada fechada", () => {
  assert.equal(canTakeAttendance({ ...match, status: "cancelled" }, new Date(match.startsAt)), "closed");
});

test("mensagem da chamada usa o código do problem e preserva outros erros", () => {
  for (const [status, code, message] of [
    [403, "not-organizer", "Só o organizador pode fazer a chamada."],
    [409, "attendance-not-open", "A chamada abre no início da partida."],
    [409, "attendance-closed", "A chamada fechou."],
    [404, "attendance-participant-not-found", "Este participante não tem inscrição confirmada nesta partida."],
    [409, "match-cancelled", "A partida foi cancelada."],
  ] as const) {
    assert.equal(attendanceMessage(new ApiError(status, "Erro", code)), message);
  }
  assert.equal(attendanceMessage(new ApiError(429, "Aguarde 2 s antes de tentar novamente.")), "Aguarde 2 s antes de tentar novamente.");
});

test("appApi mantém o código recebido em code ou no tipo de problem da API", async (t) => {
  const code = "attendance-closed";
  for (const problem of [{ type: `urn:applada:problem:${code}` }, { code }]) {
    const fetch = t.mock.method(globalThis, "fetch", async () => Response.json(problem, { status: 409 }));
    await assert.rejects(appApi("matches/partida/attendance"), (error) => error instanceof ApiError && error.code === code);
    fetch.mock.restore();
  }
});

test("403 sem código reconhecido explica a falta de permissão para a chamada", () => {
  for (const code of [undefined, "outro-erro"]) {
    assert.equal(attendanceMessage(new ApiError(403, "Forbidden", code)), "Você não tem permissão para fazer esta chamada.");
  }
});

test("409 sem código reconhecido explica o conflito em português", () => {
  for (const code of [undefined, "outro-erro"]) {
    assert.equal(attendanceMessage(new ApiError(409, "Conflict", code)), "Não foi possível atualizar a chamada. Recarregue a página e tente novamente.");
  }
});
