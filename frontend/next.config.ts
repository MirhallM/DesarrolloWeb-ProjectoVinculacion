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

    // Todo /api/* se reenvía al backend (en Railway, por la red privada). Las rutas propias de Next.js
    // (src/app/api/...) tienen prioridad: estas reglas solo aplican cuando no hay un archivo que coincida.
    // OJO: las reglas se calculan en "next build", así que API_URL debe existir también al compilar.
    return [
      {
        source: "/api/:path*",
        destination: `${backendOrigin}/api/:path*`,
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
