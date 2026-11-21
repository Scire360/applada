import assert from "node:assert/strict";
import { test } from "node:test";
import { isAllowedResponseType, isJsonRequest, isSameOrigin, maxJsonBytes, readBoundedBody } from "../../src/lib/bff/guards.ts";

function stream(chunks: Uint8Array[]) {
  return new ReadableStream<Uint8Array>({
    start(controller) {
      for (const chunk of chunks) controller.enqueue(chunk);
      controller.close();
    },
  });
}

test("isSameOrigin", () => {
  assert.equal(isSameOrigin(null, "http://localhost:3000"), false);
  assert.equal(isSameOrigin("https://evil.test", "http://localhost:3000"), false);
  assert.equal(isSameOrigin("http://localhost:3000", "http://localhost:3000"), true);
});

test("isJsonRequest", () => {
  assert.equal(isJsonRequest("application/json"), true);
  assert.equal(isJsonRequest("Application/JSON; charset=utf-8"), true);
  assert.equal(isJsonRequest("text/plain"), false);
  assert.equal(isJsonRequest("application/problem+json"), false);
  assert.equal(isJsonRequest(null), false);
});

test("isAllowedResponseType", () => {
  assert.equal(isAllowedResponseType("application/json; charset=utf-8"), true);
  assert.equal(isAllowedResponseType("application/problem+json"), true);
  assert.equal(isAllowedResponseType("text/html"), false);
  assert.equal(isAllowedResponseType("application/jsonx"), false);
  assert.equal(isAllowedResponseType(null), false);
});

test("maxJsonBytes é 16 KB", () => {
  assert.equal(maxJsonBytes, 16 * 1024);
});

test("readBoundedBody: acima do limite devolve null e cancela o stream", async () => {
  let cancelled = false;
  const body = new ReadableStream<Uint8Array>({
    pull(controller) { controller.enqueue(new Uint8Array(4096)); },
    cancel() { cancelled = true; },
  });
  assert.equal(await readBoundedBody(body, maxJsonBytes), null);
  assert.equal(cancelled, true);
});

test("readBoundedBody: exatamente o limite + 1 em pedaços", async () => {
  const chunks = [new Uint8Array(maxJsonBytes), new Uint8Array(1)];
  assert.equal(await readBoundedBody(stream(chunks), maxJsonBytes), null);
});

test("readBoundedBody: corpo pequeno e vazio", async () => {
  const small = await readBoundedBody(stream([new Uint8Array([1, 2, 3]), new Uint8Array([4, 5, 6, 7, 8, 9, 10])]), maxJsonBytes);
  assert.deepEqual(Array.from(small ?? []), [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
  assert.deepEqual(Array.from((await readBoundedBody(null, maxJsonBytes)) ?? [1]), []);
});
