import assert from "node:assert/strict";
import { test } from "node:test";
import { canLeave, canManageInvitations, invitationFromHash, keepInvitation, memberActions } from "../../src/lib/groups.ts";
import { shouldClearInvitationCookie } from "../../src/lib/bff/invitation.ts";

const token = "A".repeat(43);

test("invitationFromHash", () => {
  assert.equal(invitationFromHash(`#${token}`), token);
  assert.equal(invitationFromHash(token), null);
  assert.equal(invitationFromHash(""), null);
  assert.equal(invitationFromHash("#abc"), null);
  assert.equal(invitationFromHash(`#${token}x`), null);
  assert.equal(invitationFromHash(`#${"é".repeat(43)}`), null);
});

test("memberActions: owner", () => {
  assert.deepEqual(memberActions("owner", "a", { userId: "b", role: "member" }), { remove: true, role: "admin" });
  assert.deepEqual(memberActions("owner", "a", { userId: "b", role: "admin" }), { remove: true, role: "member" });
  assert.deepEqual(memberActions("owner", "a", { userId: "a", role: "owner" }), { remove: false, role: null });
});

test("memberActions: admin so remove member", () => {
  assert.deepEqual(memberActions("admin", "a", { userId: "b", role: "member" }), { remove: true, role: null });
  assert.deepEqual(memberActions("admin", "a", { userId: "b", role: "admin" }), { remove: false, role: null });
  assert.deepEqual(memberActions("admin", "a", { userId: "b", role: "owner" }), { remove: false, role: null });
  assert.deepEqual(memberActions("admin", "a", { userId: "a", role: "admin" }), { remove: false, role: null });
});

test("memberActions: member nao faz nada", () => {
  assert.deepEqual(memberActions("member", "a", { userId: "b", role: "member" }), { remove: false, role: null });
});

test("canLeave e canManageInvitations", () => {
  assert.equal(canLeave("owner"), false);
  assert.equal(canLeave("admin"), true);
  assert.equal(canLeave("member"), true);
  assert.equal(canManageInvitations("owner"), true);
  assert.equal(canManageInvitations("admin"), true);
  assert.equal(canManageInvitations("member"), false);
});

test("shouldClearInvitationCookie", () => {
  for (const status of [200, 201, 204, 400, 404, 409]) assert.equal(shouldClearInvitationCookie(status), true, String(status));
  for (const status of [401, 403, 429, 500, 502, 503]) assert.equal(shouldClearInvitationCookie(status), false, String(status));
});

test("keepInvitation: segunda leitura com hash vazio mantém o token (Strict Mode)", () => {
  const first = keepInvitation(null, `#${token}`);
  assert.equal(first, token);
  assert.equal(keepInvitation(first, ""), token);
  assert.equal(keepInvitation(null, ""), null);
  assert.equal(keepInvitation(token, `#${"B".repeat(43)}`), token);
});
