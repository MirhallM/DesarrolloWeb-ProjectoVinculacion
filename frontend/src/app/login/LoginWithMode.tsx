"use client";

import { useSearchParams } from "next/navigation";
import { LoginForm } from "@/components/auth/LoginForm";

export function LoginWithMode() {
  const mode =
    useSearchParams().get("modo") === "registro" ? "register" : "login";
  return <LoginForm key={mode} initialMode={mode} />;
}
