"use client";

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { useRouter } from "next/navigation";
import { DEFAULT_BANNERS, DEFAULT_FEATURED } from "@/lib/data";
import type { Banner, UserSession } from "@/types";

type Updater<T> = (fn: (prev: T) => T) => void;

interface Store {
  user: UserSession | null;
  login: (email: string, name?: string) => void;
  logout: () => void;
  favorites: string[];
  toggleFavorite: (id: string) => void;
  isFavorite: (id: string) => boolean;
  banners: Banner[];
  setBanners: Updater<Banner[]>;
  featured: Record<string, boolean>;
  setFeatured: Updater<Record<string, boolean>>;
  query: string;
  setQuery: (q: string) => void;
  /** Redirige al visitante a /login y regresa a `next` tras autenticarse. */
  requireLogin: (next: string, reason?: string) => void;
  loginReason: string | null;
  resetLoginIntent: () => void;
}

const StoreContext = createContext<Store | null>(null);

/**
 * Proveedor de estado de la interfaz.
 * Nota de seguridad (frontend/CONTEXT.md): Prohibido almacenar tokens JWT
 * o sesiones sensibles en localStorage/sessionStorage. La sesión real se
 * gestiona mediante cookies HTTP-only en /api/auth.
 */
export function StoreProvider({ children }: { children: ReactNode }) {
  const router = useRouter();
  const [user, setUser] = useState<UserSession | null>(null);
  const [favorites, setFavorites] = useState<string[]>([]);
  const [banners, setBannersState] = useState<Banner[]>(DEFAULT_BANNERS);
  const [featured, setFeaturedState] =
    useState<Record<string, boolean>>(DEFAULT_FEATURED);
  const [query, setQuery] = useState("");
  const [redirectTo, setRedirectTo] = useState("/");
  const [loginReason, setLoginReason] = useState<string | null>(null);

  const resetLoginIntent = useCallback(() => {
    setRedirectTo("/");
    setLoginReason(null);
  }, []);

  const requireLogin = useCallback(
    (next: string, reason?: string) => {
      setRedirectTo(next);
      setLoginReason(reason ?? null);
      router.push("/login");
    },
    [router]
  );

  const login = useCallback(
    (email: string, name?: string) => {
      const local = email.split("@")[0];
      setUser({
        email,
        name: name?.trim() || local.charAt(0).toUpperCase() + local.slice(1),
        isAdmin: local.toLowerCase().startsWith("admin"),
      });
      router.push(redirectTo);
      resetLoginIntent();
    },
    [router, redirectTo, resetLoginIntent]
  );

  const logout = useCallback(() => {
    setUser(null);
    router.push("/");
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
      login,
      logout,
      favorites,
      toggleFavorite,
      isFavorite: (id: string) => favorites.includes(id),
      banners,
      setBanners: setBannersState,
      featured,
      setFeatured: setFeaturedState,
      query,
      setQuery,
      requireLogin,
      loginReason,
      resetLoginIntent,
    }),
    [
      user,
      login,
      logout,
      favorites,
      toggleFavorite,
      banners,
      featured,
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

