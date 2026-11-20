import assert from "node:assert/strict";
import test from "node:test";

const origin = process.env.APPLADA_E2E_WEB_ORIGIN ?? "http://localhost:3000";

test("BFF retorna Problem Details para sessão ausente, sem redirecionar ao login", async () => {
  const response = await fetch(`${origin}/api/backend/session`, { redirect: "manual" });

  assert.equal(response.status, 401);
  assert.match(response.headers.get("content-type") ?? "", /^application\/problem\+json(?:;|$)/i);
  assert.match(response.headers.get("cache-control") ?? "", /private, no-store/i);
  assert.deepEqual(await response.json(), {
    status: 401,
    title: "Entre novamente para continuar.",
  });
});

test("API local não redireciona uma sessão ausente para o login Auth.js", async () => {
  const response = await fetch(`${origin}/api/local/session`, { redirect: "manual" });

  assert.ok(response.status === 401 || response.status === 404);
  assert.match(response.headers.get("content-type") ?? "", /^application\/problem\+json(?:;|$)/i);

  if (response.status === 401) {
    const page = await fetch(`${origin}/eventos/novo`, { redirect: "manual" });
    assert.equal(page.status, 200);
  }
});

test("BFF recusa escrita com origem externa antes de chamar a API", async () => {
  const response = await fetch(`${origin}/api/backend/matches`, {
    method: "POST",
    headers: {
      Origin: "https://origem-invalida.example",
      "Content-Type": "application/json",
    },
    body: "{}",
    redirect: "manual",
  });

  assert.equal(response.status, 403);
  assert.match(response.headers.get("content-type") ?? "", /^application\/problem\+json(?:;|$)/i);
});
