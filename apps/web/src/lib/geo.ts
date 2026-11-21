export type BrowserPosition = { lat: number; lon: number };

const hundredths = new Intl.NumberFormat("en-US", { maximumFractionDigits: 2, useGrouping: false });

export function roundCoordinate(value: number): number {
  const rounded = Number(hundredths.format(value));
  return rounded === 0 ? 0 : rounded;
}

export const positionHeaders = { lat: "X-Applada-Lat", lon: "X-Applada-Lon" } as const;
export const deniedMessage = "Localização negada. Libere a localização nas permissões deste site no navegador e tente novamente.";

export async function readBrowserPosition(): Promise<BrowserPosition | "denied" | "unavailable"> {
  try {
    const geo = globalThis.navigator?.geolocation;
    if (!geo) return "unavailable";
    return await new Promise<BrowserPosition | "denied" | "unavailable">((resolve) => {
      geo.getCurrentPosition(
        ({ coords }) => resolve({ lat: roundCoordinate(coords.latitude), lon: roundCoordinate(coords.longitude) }),
        (error) => resolve(error.code === error.PERMISSION_DENIED ? "denied" : "unavailable"),
        { enableHighAccuracy: false, timeout: 10_000 },
      );
    });
  } catch {
    return "unavailable";
  }
}
