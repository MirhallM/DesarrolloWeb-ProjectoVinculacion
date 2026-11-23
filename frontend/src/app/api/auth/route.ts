import { NextResponse } from "next/server";

/**
 * Endpoint BFF (Backend for Frontend) para gestión de sesión con cookies HTTP-only.
 */
export async function POST() {
  return NextResponse.json(
    { message: "Auth endpoint pendiente" },
    { status: 501 }
  );
}
