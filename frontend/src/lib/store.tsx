"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { useRouter } from "next/navigation";
import {
  getCurrentUser,
  loginUser,
  logoutUser,
  registerUser,
  toUserSession,
} from "@/lib/auth";
import type {
  Banner,
  LoginInput,
  RegisterInput,
  UserSession,
} from "@/types";

const DEFAULT_BANNERS: Banner[] = [
  {
    id: "b1",
    title: "Semana fresca: hasta 30% de descuento en frutas y verduras",
    subtitle: "Productos recién cosechados, reabastecidos cada mañana.",
    cta: "Ver ofertas frescas",
    image:
      "https://images.unsplash.com/photo-1609126986933-e3c84f19d49c?auto=format&fit=crop&w=1600&q=80",
    active: true,
    createdAt: "2026-05-02",
  },
  {
    id: "b2",
    title: "Pan calientito de nuestra panadería, todos los días",
    subtitle:
      "Masa madre, panecillos y pan dulce horneados en tienda desde las 6 a.m.",
    cta: "Ver la panadería",
    image:
      "https://images.unsplash.com/photo-1559811814-e2c57b5e69df?auto=format&fit=crop&w=1600&q=80",
    active: true,
    createdAt: "2026-04-21",
  },
];

function sanitizeRedirectPath(path: string): string {
  return path.startsWith("/") && !path.startsWith("//") ? path : "/";
}

interface Store {
  user: UserSession | null;
  authLoading: boolean;
  login: (input: LoginInput) => Promise<void>;
  register: (input: RegisterInput) => Promise<void>;
  logout: () => Promise<void>;
  favorites: string[];
  toggleFavorite: (id: string) => void;
  isFavorite: (id: string) => boolean;
  banners: Banner[];
  query: string;
  setQuery: (q: string) => void;
  requireLogin: (next: string, reason?: string) => void;
  loginReason: string | null;
  resetLoginIntent: () => void;
}

const StoreContext = createContext<Store | null>(null);

export function StoreProvider({ children }: { children: ReactNode }) {
  const router = useRouter();
  const [user, setUser] = useState<UserSession | null>(null);
  const [authLoading, setAuthLoading] = useState<boolean>(true);
  const [favorites, setFavorites] = useState<string[]>([]);
  const [banners] = useState<Banner[]>(DEFAULT_BANNERS);
  const [query, setQuery] = useState("");
  const [redirectTo, setRedirectTo] = useState("/");
  const [loginReason, setLoginReason] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();

    getCurrentUser({ signal: controller.signal })
      .then((authUser) => {
        setUser(authUser ? toUserSession(authUser) : null);
      })
      .catch(() => {
        // Si el backend no responde al iniciar, el catálogo público sigue operando como visitante.
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setAuthLoading(false);
        }
      });

    return () => controller.abort();
  }, []);

  const resetLoginIntent = useCallback(() => {
    setRedirectTo("/");
    setLoginReason(null);
  }, []);

  const requireLogin = useCallback(
    (next: string, reason?: string) => {
      setRedirectTo(sanitizeRedirectPath(next));
      setLoginReason(reason ?? null);
      router.push("/login");
    },
    [router]
  );

  const login = useCallback(
    async (input: LoginInput) => {
      const authUser = await loginUser(input);
      setUser(toUserSession(authUser));
      const destination = sanitizeRedirectPath(redirectTo);
      resetLoginIntent();
      router.push(destination);
    },
    [router, redirectTo, resetLoginIntent]
  );

  const register = useCallback(
    async (input: RegisterInput) => {
      const authUser = await registerUser(input);
      setUser(toUserSession(authUser));
      const destination = sanitizeRedirectPath(redirectTo);
      resetLoginIntent();
      router.push(destination);
    },
    [router, redirectTo, resetLoginIntent]
  );

  const logout = useCallback(async () => {
    try {
      await logoutUser();
    } catch {
      // Aunque falle la red al cerrar sesión, se limpia el estado local.
    } finally {
      setUser(null);
      setFavorites([]);
      router.push("/");
    }
  }, [router]);

  const toggleFavorite = useCallback(
    (id: string) =>
      setFavorites((prev) =>
        prev.includes(id) ? prev.filter((item) => item !== id) : [...prev, id]
      ),
    []
  );

  const value = useMemo<Store>(
    () => ({
      user,
      authLoading,
      login,
      register,
      logout,
      favorites,
      toggleFavorite,
      isFavorite: (id: string) => favorites.includes(id),
      banners,
      query,
      setQuery,
      requireLogin,
      loginReason,
      resetLoginIntent,
    }),
    [
      user,
      authLoading,
      login,
      register,
      logout,
      favorites,
      toggleFavorite,
      banners,
      query,
      requireLogin,
      loginReason,
      resetLoginIntent,
    ]
  );

  return (
    <StoreContext.Provider value={value}>{children}</StoreContext.Provider>
  );
}

export function useStore() {
  const ctx = useContext(StoreContext);
  if (!ctx) throw new Error("useStore debe usarse dentro de <StoreProvider>");
  return ctx;
}
