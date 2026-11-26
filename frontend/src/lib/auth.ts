import { ApiError, apiFetch } from "@/lib/api";
import type {
  AuthUser,
  LoginInput,
  RegisterInput,
  UserSession,
} from "@/types";

export function toUserSession(authUser: AuthUser): UserSession {
  const localPart = authUser.email.split("@")[0] || authUser.email;
  const displayName =
    localPart.charAt(0).toUpperCase() + localPart.slice(1).toLowerCase();

  return {
    id: authUser.id,
    email: authUser.email,
    name: displayName,
    roles: authUser.roles,
    permissions: authUser.permissions ?? [],
    isAdmin: authUser.roles.includes("Admin"),
  };
}

export async function registerUser(input: RegisterInput): Promise<AuthUser> {
  return apiFetch<AuthUser>("/auth/register", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      email: input.email.trim(),
      password: input.password,
    }),
  });
}

export async function loginUser(input: LoginInput): Promise<AuthUser> {
  return apiFetch<AuthUser>("/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      email: input.email.trim(),
      password: input.password,
      rememberMe: Boolean(input.rememberMe),
    }),
  });
}

export async function logoutUser(): Promise<void> {
  await apiFetch<void>("/auth/logout", {
    method: "POST",
  });
}

export async function getCurrentUser(
  options?: RequestInit
): Promise<AuthUser | null> {
  try {
    return await apiFetch<AuthUser>("/auth/me", {
      cache: "no-store",
      ...options,
    });
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return null;
    }
    throw error;
  }
}
