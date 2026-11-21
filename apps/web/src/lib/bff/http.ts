import "server-only";

import { NextResponse } from "next/server";

export function secureResponse(response: NextResponse) {
  response.headers.set("Cache-Control", "private, no-store");
  response.headers.set("Referrer-Policy", "no-referrer");
  response.headers.set("X-Content-Type-Options", "nosniff");
  return response;
}

// Erro no formato problem+json, só com status e título (sem detalhe interno).
export function problem(status: number, title: string) {
  return secureResponse(NextResponse.json({ status, title }, {
    status,
    headers: { "Content-Type": "application/problem+json" },
  }));
}
