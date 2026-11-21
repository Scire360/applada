import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  devIndicators: false,
  // Não ligar logging.fetches: as URLs de saída do BFF levam lat/lon ao terminal.
  logging: { incomingRequests: { ignore: [/\/api\/local\/callback(?:\?|$)/, /\/api\/app\/discover\/matches\/?(?:\?|$)/] } },
};

export default nextConfig;
