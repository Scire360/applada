import assert from "node:assert/strict";
import { test } from "node:test";
import config from "../../next.config.ts";

test("o log de desenvolvimento omite a descoberta e o callback", () => {
  const logging = config.logging;
  assert.ok(logging && typeof logging.incomingRequests === "object");
  assert.ok(!logging.fetches);
  const filters = logging.incomingRequests.ignore;
  assert.ok(filters);
  const ignored = (url: string) => filters.some((rule) => rule.test(url));
  for (const url of ["/api/local/callback?code=x", "/api/app/discover/matches", "/api/app/discover/matches/?lat=1&lon=2"]) {
    assert.ok(ignored(url), url);
  }
  assert.equal(ignored("/api/app/discover/matches-extra"), false);
});
