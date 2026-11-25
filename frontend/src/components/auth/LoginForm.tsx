"use client";

import { useEffect, useState, type FormEvent } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, Eye, EyeOff, Info } from "@/components/ui/Icons";
import { Logo } from "@/components/ui/Logo";
import { ApiError } from "@/lib/api";
import { useStore } from "@/lib/store";

export interface LoginFormProps {
  initialMode?: "login" | "register";
}

function validateForm(
  email: string,
  password: string,
  isLogin: boolean
): string | null {
  const trimmedEmail = email.trim();
  if (!trimmedEmail) {
    return "El correo electrónico es obligatorio.";
  }

  if (trimmedEmail.length > 256 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail)) {
    return "Ingresa un correo electrónico válido.";
  }

  if (!password) {
    return "La contraseña es obligatoria.";
  }

  if (password.length > 128) {
    return "La contraseña no puede superar los 128 caracteres.";
  }

  if (!isLogin) {
    if (password.length < 8) {
      return "La contraseña debe tener al menos 8 caracteres.";
    }
    if (!/\p{L}/u.test(password)) {
      return "La contraseña debe incluir al menos una letra.";
    }
    if (!/\d/.test(password)) {
      return "La contraseña debe incluir al menos un número.";
    }
    if (new Set(password).size < 4) {
      return "La contraseña debe contener al menos 4 caracteres distintos.";
    }
  }

  return null;
}

export function LoginForm({ initialMode = "login" }: LoginFormProps) {
  const router = useRouter();
  const { user, authLoading, login, register, loginReason, resetLoginIntent } =
    useStore();

  const [mode, setMode] = useState<"login" | "register">(initialMode);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [rememberMe, setRememberMe] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isLogin = mode === "login";

  useEffect(() => {
    if (!authLoading && user && !submitting) {
      router.replace("/");
    }
  }, [authLoading, user, submitting, router]);

  const handleSubmit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();

    const validationError = validateForm(email, password, isLogin);
    if (validationError) {
      setError(validationError);
      return;
    }

    setError(null);
    setSubmitting(true);

    try {
      if (isLogin) {
        await login({ email: email.trim(), password, rememberMe });
      } else {
        await register({ email: email.trim(), password });
      }
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message);
      } else {
        setError("Ocurrió un problema inesperado. Intenta de nuevo.");
      }
    } finally {
      setSubmitting(false);
    }
  };

  const inputClass =
    "h-12 w-full rounded-xl border border-line bg-white px-4 text-[15px] text-ink outline-none transition placeholder:text-muted/70 focus:border-brand focus:ring-4 focus:ring-brand/15 disabled:cursor-not-allowed disabled:bg-canvas/70";

  return (
    <main className="relative flex min-h-dvh flex-col items-center justify-center bg-canvas px-4 pt-20 pb-12 sm:pt-12">
      <Link
        href="/"
        onClick={resetLoginIntent}
        className="absolute top-4 left-4 inline-flex items-center gap-2 rounded-full px-3 py-2 text-sm font-semibold text-ink-soft transition hover:bg-white hover:text-ink focus-visible:outline-2 focus-visible:outline-brand sm:top-6 sm:left-6"
      >
        <ArrowLeft className="size-4" />
        Volver a la tienda
      </Link>

      <div className="w-full max-w-[420px] rounded-3xl bg-white p-6 shadow-[0_24px_60px_-24px_rgba(31,36,33,0.25)] ring-1 ring-black/5 sm:p-10">
        <div className="flex justify-center">
          <Logo className="h-14 sm:h-16" eager />
        </div>

        <h1 className="mt-6 text-center text-2xl font-extrabold tracking-tight text-ink sm:mt-7">
          {isLogin ? "Bienvenido de nuevo" : "Crea tu cuenta"}
        </h1>
        <p className="mt-1.5 text-center text-sm text-muted">
          {isLogin
            ? "Inicia sesión para consultar y guardar tus productos favoritos."
            : "Regístrate con tu correo para guardar tus productos favoritos."}
        </p>

        {loginReason && (
          <div className="mt-6 flex gap-2.5 rounded-xl bg-brand-50 px-4 py-3 text-sm text-brand-deep">
            <Info className="mt-0.5 size-4 shrink-0" />
            <span>{loginReason}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="mt-6 space-y-4" noValidate>
          <div>
            <label
              htmlFor="email"
              className="mb-1.5 block text-sm font-semibold text-ink"
            >
              Correo electrónico
            </label>
            <input
              id="email"
              type="email"
              inputMode="email"
              value={email}
              disabled={submitting}
              onChange={(e) => setEmail(e.target.value)}
              className={inputClass}
              placeholder="tu@correo.com"
              autoComplete="email"
              required
            />
          </div>

          <div>
            <label
              htmlFor="password"
              className="mb-1.5 block text-sm font-semibold text-ink"
            >
              Contraseña
            </label>
            <div className="relative">
              <input
                id="password"
                type={showPassword ? "text" : "password"}
                value={password}
                disabled={submitting}
                onChange={(e) => setPassword(e.target.value)}
                className={`${inputClass} pr-12`}
                placeholder="••••••••"
                autoComplete={isLogin ? "current-password" : "new-password"}
                required
              />
              <button
                type="button"
                onClick={() => setShowPassword((prev) => !prev)}
                aria-label={
                  showPassword ? "Ocultar contraseña" : "Mostrar contraseña"
                }
                className="absolute top-1/2 right-2 grid size-9 -translate-y-1/2 place-items-center rounded-lg text-muted transition hover:bg-canvas hover:text-ink focus-visible:outline-2 focus-visible:outline-brand"
              >
                {showPassword ? (
                  <EyeOff className="size-4" />
                ) : (
                  <Eye className="size-4" />
                )}
              </button>
            </div>
            {!isLogin && (
              <p className="mt-1.5 text-xs text-muted">
                Mínimo 8 caracteres, incluyendo al menos una letra y un número.
              </p>
            )}
          </div>

          {isLogin && (
            <label className="flex cursor-pointer items-center gap-2.5 select-none">
              <input
                type="checkbox"
                checked={rememberMe}
                disabled={submitting}
                onChange={(e) => setRememberMe(e.target.checked)}
                className="size-4 rounded border-line accent-brand focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand"
              />
              <span className="text-sm text-ink-soft">
                Mantener sesión iniciada
              </span>
            </label>
          )}

          {error && (
            <p
              role="alert"
              className="rounded-xl bg-red-50 px-3.5 py-2.5 text-sm font-medium text-red-700 ring-1 ring-red-200"
            >
              {error}
            </p>
          )}

          <button
            type="submit"
            disabled={submitting}
            className="mt-2 h-13 w-full rounded-xl bg-accent text-base font-bold text-white shadow-[0_8px_20px_-8px_rgba(251,120,6,0.7)] transition hover:bg-accent-dark focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent active:scale-[0.99] disabled:cursor-not-allowed disabled:opacity-65"
          >
            {submitting
              ? isLogin
                ? "Iniciando sesión..."
                : "Creando cuenta..."
              : isLogin
                ? "Iniciar sesión"
                : "Crear cuenta"}
          </button>
        </form>

        <p className="mt-6 text-center text-sm text-muted">
          {isLogin ? "¿No tienes una cuenta? " : "¿Ya tienes una cuenta? "}
          <button
            type="button"
            disabled={submitting}
            onClick={() => {
              setMode(isLogin ? "register" : "login");
              setError(null);
            }}
            className="font-bold text-brand-dark hover:underline focus-visible:outline-2 focus-visible:outline-brand"
          >
            {isLogin ? "Regístrate" : "Inicia sesión"}
          </button>
        </p>
      </div>
    </main>
  );
}
