import assert from "node:assert/strict";
import { test } from "node:test";
import { loginHref, safeCallbackPath } from "../../src/lib/callback-url.ts";

test("safeCallbackPath aceita só caminho relativo da mesma origem", () => {
  assert.equal(safeCallbackPath("/grupos"), "/grupos");
  assert.equal(safeCallbackPath("/grupos/abc?x=1#y"), "/grupos/abc?x=1#y");
  assert.equal(safeCallbackPath("/peladas/conectadas?grupo=1"), "/peladas/conectadas?grupo=1");
});

test("safeCallbackPath recusa origem externa, esquemas e formas ambíguas", () => {
  for (const value of ["https://evil.test/", "//evil.test", "/\\evil.test", "\\\\evil.test", "javascript:alert(1)", "grupos", "", "/a\nb", "/a\\b", "/%0d%0a"]) {
    assert.equal(safeCallbackPath(value), "/", value);
  }
  assert.equal(safeCallbackPath(undefined), "/");
  assert.equal(safeCallbackPath(["/grupos", "/x"]), "/");
  assert.equal(safeCallbackPath("/" + "a".repeat(2100)), "/");
});

test("safeCallbackPath não volta para o login", () => {
  assert.equal(safeCallbackPath("/login"), "/");
  assert.equal(safeCallbackPath("/login?callbackUrl=/grupos"), "/");
});

test("loginHref monta o link com o caminho atual codificado", () => {
  assert.equal(loginHref("/grupos/abc", "?a=1"), "/login?callbackUrl=%2Fgrupos%2Fabc%3Fa%3D1");
  assert.equal(loginHref("/grupos", ""), "/login?callbackUrl=%2Fgrupos");
});
