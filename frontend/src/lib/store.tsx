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
import type { Banner, UserSession } from "@/types";

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

interface Store {
  user: UserSession | null;
  login: (email: string, name?: string) => void;
  logout: () => void;
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
  const [favorites, setFavorites] = useState<string[]>([]);
  const [banners] = useState<Banner[]>(DEFAULT_BANNERS);
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
