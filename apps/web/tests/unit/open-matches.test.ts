import assert from "node:assert/strict";
import { test } from "node:test";
import { discoveryResultMessage } from "../../src/lib/connected-matches.ts";

test("a busca anuncia a quantidade de peladas da lista ou orienta a ampliar o raio", () => {
  assert.equal(discoveryResultMessage(0), "Nenhuma pelada encontrada neste raio. Experimente aumentar a distância.");
  assert.equal(discoveryResultMessage(1), "1 pelada encontrada");
  assert.equal(discoveryResultMessage(21), "21 peladas encontradas");
});
