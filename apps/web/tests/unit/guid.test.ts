import assert from "node:assert/strict";
import { test } from "node:test";
import { guidPattern } from "../../src/lib/guid.ts";

test("valida o GUID completo com hífens, em maiúsculas ou minúsculas", () => {
  const id = "018f1234-abcd-7000-8000-123456789abc";
  assert.ok(guidPattern.test(id));
  assert.ok(guidPattern.test(id.toUpperCase()));
  for (const invalid of [id.replaceAll("-", ""), `{${id}}`, `${id}/outro`, id.slice(1)]) {
    assert.equal(guidPattern.test(invalid), false);
  }
});
