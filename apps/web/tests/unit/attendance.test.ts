import assert from "node:assert/strict";
import { test } from "node:test";
import { attendanceLinkAction, attendanceMessage, attendanceProblem, canTakeAttendance, readAttendance } from "../../src/lib/attendance.ts";
import { ApiError, appApi } from "../../src/lib/api-client.ts";

const match = { startsAt: "2026-11-21T19:00:00-03:00", durationMinutes: 90, status: "scheduled" };

test("organizador conhecido vê o link na janela aberta sem consultar detalhes", () => {
  assert.equal(attendanceLinkAction("open", true), "link");
  assert.equal(attendanceLinkAction("open", true, new ApiError(503, "Indisponível")), "link");
  assert.equal(attendanceLinkAction("open", false), "hidden");
  assert.equal(attendanceLinkAction("open", undefined), "load");
});

test("consulta da chamada oferece nova tentativa apenas em falha de rede ou 5xx", () => {
  for (const status of [0, 500, 503, 599]) {
    assert.equal(attendanceLinkAction("open", undefined, new ApiError(status, "Erro")), "retry");
  }
  for (const status of [401, 403, 404, 429, 499]) {
    assert.equal(attendanceLinkAction("open", undefined, new ApiError(status, "Erro")), "hidden");
  }
  assert.equal(attendanceLinkAction("open", undefined, new SyntaxError("JSON inválido")), "hidden");
});

test("interrupção do corpo da resposta permite tentar a consulta da chamada de novo", async (t) => {
  const body = new ReadableStream({ start(controller) { controller.error(new TypeError("Conexão interrompida")); } });
  t.mock.method(globalThis, "fetch", async () => new Response(body));
  await assert.rejects(appApi("matches/partida"), (error) => {
    assert.equal(attendanceLinkAction("open", undefined, error), "retry");
    return true;
  });
});

test("consulta usa o status HTTP quando a resposta de erro traz JSON nulo", async (t) => {
  const fetch = t.mock.method(globalThis, "fetch");
  for (const [status, action] of [[403, "hidden"], [503, "retry"]] as const) {
    fetch.mock.mockImplementation(async () => new Response("null", { status }));
    await assert.rejects(appApi("matches/partida"), (error) => {
      assert.equal(attendanceLinkAction("open", undefined, error), action);
      return true;
    });
  }
});

test("fora da janela a chamada fica oculta mesmo para o organizador ou após um 503", () => {
  for (const phase of ["not-open", "closed"] as const) {
    assert.equal(attendanceLinkAction(phase, true), "hidden");
    assert.equal(attendanceLinkAction(phase, undefined), "hidden");
    assert.equal(attendanceLinkAction(phase, undefined, new ApiError(503, "Indisponível")), "hidden");
  }
});

test("erro de acesso em uma ação prevalece sobre o 503 da leitura anterior", () => {
  const unavailable = new ApiError(503, "Indisponível");
  for (const status of [401, 403]) {
    const denied = new ApiError(status, "Sem acesso");
    assert.equal(attendanceProblem(unavailable, denied), denied);
    assert.equal(attendanceProblem(denied, unavailable), denied);
  }
});

test("chamada preserva o 503 isolado e fica sem erro quando as operações têm sucesso", () => {
  const unavailable = new ApiError(503, "Indisponível");
  assert.equal(attendanceProblem(unavailable, undefined), unavailable);
  assert.equal(attendanceProblem(undefined, undefined), undefined);
});

test("recarga preserva o 403 da lista mesmo se os detalhes responderem 503 primeiro", async () => {
  const list = Promise.withResolvers<never>();
  const denied = new ApiError(403, "Sem acesso");
  const result = readAttendance(list.promise, Promise.reject(new ApiError(503, "Indisponível")));
  const rejected = assert.rejects(result, (error) => error === denied);
  await new Promise<void>((resolve) => setImmediate(resolve));
  list.reject(denied);
  await rejected;
});

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

test("appApi extrai o código do tipo de problem da API", async (t) => {
  const code = "attendance-closed";
  const problem = { type: `urn:applada:problem:${code}` };
  t.mock.method(globalThis, "fetch", async () => Response.json(problem, { status: 409 }));
  await assert.rejects(appApi("matches/partida/attendance"), (error) => error instanceof ApiError && error.code === code);
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
