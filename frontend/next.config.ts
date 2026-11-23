import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  images: {
    remotePatterns: [
      {
        protocol: "https",
        hostname: "images.unsplash.com",
      },
    ],
  },
  async rewrites() {
    const backendOrigin = (
      process.env.API_URL ||
      process.env.BACKEND_URL ||
      "http://localhost:5039"
    )
      .replace(/\/+$/, "")
      .replace(/\/api$/i, "");

    return [
      {
        source: "/api/products/:path*",
        destination: `${backendOrigin}/api/products/:path*`,
      },
      {
        source: "/api/products",
        destination: `${backendOrigin}/api/products`,
      },
      {
        source: "/api/categories",
        destination: `${backendOrigin}/api/categories`,
      },
    ];
  },
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
