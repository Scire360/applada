import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  cacheComponents: true,
  partialPrefetching: true,
  logging: { incomingRequests: { ignore: [/\/api\/local\/callback(?:\?|$)/] } },
};

export default nextConfig;
