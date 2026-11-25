import { Suspense } from "react";
import type { Metadata } from "next";
import { LoginForm } from "@/components/auth/LoginForm";
import { LoginWithMode } from "./LoginWithMode";

export const metadata: Metadata = {
  title: "Iniciar sesión",
};

export default function LoginPage() {
  return (
    <Suspense fallback={<LoginForm />}>
      <LoginWithMode />
    </Suspense>
  );
}
