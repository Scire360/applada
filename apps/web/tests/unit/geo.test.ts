import assert from "node:assert/strict";
import { test } from "node:test";
import { readBrowserPosition, roundCoordinate } from "../../src/lib/geo.ts";

test("arredonda coordenadas a duas casas, inclusive empates negativos", () => {
  for (const [value, expected] of [[-20.469712, -20.47], [0.005, 0.01], [-0.005, -0.01], [1.005, 1.01], [-0.001, 0]]) {
    assert.equal(roundCoordinate(value), expected);
  }
});

test("a leitura só entrega a posição aproximada do navegador", async (t) => {
  t.mock.getter(globalThis, "navigator", () => ({
    geolocation: {
      getCurrentPosition(success: PositionCallback) {
        success({ coords: { latitude: -20.469712, longitude: -54.620123 } } as GeolocationPosition);
      },
    },
  } as Navigator));
  assert.deepEqual(await readBrowserPosition(), { lat: -20.47, lon: -54.62 });
});

for (const [code, expected] of [[1, "denied"], [2, "unavailable"], [3, "unavailable"]] as const) {
  test(`erro de geolocalização ${code} retorna ${expected}`, async (t) => {
    t.mock.getter(globalThis, "navigator", () => ({
      geolocation: {
        getCurrentPosition(_success: PositionCallback, error: PositionErrorCallback) {
          error({ code, PERMISSION_DENIED: 1 } as GeolocationPositionError);
        },
      },
    } as Navigator));
    assert.equal(await readBrowserPosition(), expected);
  });
}

test("geolocalização ausente retorna unavailable", async (t) => {
  t.mock.getter(globalThis, "navigator", () => ({} as Navigator));
  assert.equal(await readBrowserPosition(), "unavailable");
});
