import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  devIndicators: false,
  logging: { incomingRequests: { ignore: [/\/api\/local\/callback(?:\?|$)/] } },
};

export default nextConfig;
