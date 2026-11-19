import assert from "node:assert/strict";
import test from "node:test";
import { safeReturnPath } from "../src/lib/auth/return-path.mjs";

test("keeps a safe local path with its query and fragment", () => {
  assert.equal(safeReturnPath("/peladas?status=open#agenda"), "/peladas?status=open#agenda");
});

test("rejects external, protocol-relative, and backslash-based destinations", () => {
  assert.equal(safeReturnPath("https://example.com"), "/");
  assert.equal(safeReturnPath("//example.com"), "/");
  assert.equal(safeReturnPath("/\\example.com"), "/");
  assert.equal(safeReturnPath("/..//example.com"), "/");
});

test("does not redirect a signed-in user back to the login page", () => {
  assert.equal(safeReturnPath("/login?next=/peladas"), "/");
});
