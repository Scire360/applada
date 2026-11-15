import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  cacheComponents: true,
  partialPrefetching: true,
  devIndicators: false,
  logging: { incomingRequests: { ignore: [/\/api\/local\/callback(?:\?|$)/] } },
};

export default nextConfig;
