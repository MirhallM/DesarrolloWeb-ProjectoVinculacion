"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { useRouter } from "next/navigation";
import { ConfirmRemoveFavoriteModal } from "@/components/favorites/ConfirmRemoveFavoriteModal";
import { ApiError } from "@/lib/api";
import {
  getCurrentUser,
  loginUser,
  logoutUser,
  registerUser,
  toUserSession,
} from "@/lib/auth";
import {
  addFavorite,
  getFavorites,
  removeFavorite,
} from "@/lib/favorites";
import type {
  Banner,
  FavoriteItem,
  LoginInput,
  PublicProduct,
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

const EMPTY_FAVORITE_ITEMS: FavoriteItem[] = [];

function sanitizeRedirectPath(path: string): string {
  return path.startsWith("/") && !path.startsWith("//") ? path : "/";
}

function toOptimisticFavorite(product: PublicProduct): FavoriteItem {
  return {
    productId: product.id,
    name: product.name,
    price: product.price,
    isVisible: true,
    available: product.available,
    availabilityStatus: product.available ? "available" : "out_of_stock",
    category: product.category,
    imageUrl: product.imageUrl,
    savedAt: new Date().toISOString(),
  };
}

interface FavoritesState {
  userId: string | null;
  items: FavoriteItem[];
  error: string | null;
}

interface Store {
  user: UserSession | null;
  authLoading: boolean;
  login: (input: LoginInput) => Promise<void>;
  register: (input: RegisterInput) => Promise<void>;
  logout: () => Promise<void>;
  favorites: string[];
  favoriteItems: FavoriteItem[];
  favoritesLoading: boolean;
  favoritesError: string | null;
  reloadFavorites: () => Promise<void>;
  toggleFavorite: (id: string, snapshot?: PublicProduct) => void;
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
  const [favoritesState, setFavoritesState] = useState<FavoritesState>({
    userId: null,
    items: EMPTY_FAVORITE_ITEMS,
    error: null,
  });
  const [reloadingFavorites, setReloadingFavorites] = useState<boolean>(false);
  const [banners] = useState<Banner[]>(DEFAULT_BANNERS);
  const [query, setQuery] = useState("");
  const [redirectTo, setRedirectTo] = useState("/");
  const [loginReason, setLoginReason] = useState<string | null>(null);
  const [pendingRemoval, setPendingRemoval] = useState<FavoriteItem | null>(
    null
  );

  const pendingIdsRef = useRef<Set<string>>(new Set());

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

  const loadFavoritesForUser = useCallback(
    async (activeUserId: string, signal?: AbortSignal) => {
      try {
        const items = await getFavorites({ signal });
        if (!signal?.aborted) {
          setFavoritesState({ userId: activeUserId, items, error: null });
        }
      } catch (error) {
        if (error instanceof DOMException && error.name === "AbortError") {
          return;
        }
        if (error instanceof ApiError && error.status === 401) {
          setUser(null);
          setFavoritesState({
            userId: null,
            items: EMPTY_FAVORITE_ITEMS,
            error: null,
          });
          return;
        }
        if (!signal?.aborted) {
          setFavoritesState({
            userId: activeUserId,
            items: EMPTY_FAVORITE_ITEMS,
            error:
              error instanceof ApiError
                ? error.message
                : "No fue posible cargar tus productos favoritos.",
          });
        }
      }
    },
    []
  );

  useEffect(() => {
    const activeUserId = user?.id;
    if (!activeUserId) {
      return;
    }

    const controller = new AbortController();

    getFavorites({ signal: controller.signal })
      .then((items) => {
        if (!controller.signal.aborted) {
          setFavoritesState({ userId: activeUserId, items, error: null });
        }
      })
      .catch((error) => {
        if (error instanceof DOMException && error.name === "AbortError") {
          return;
        }
        if (error instanceof ApiError && error.status === 401) {
          setUser(null);
          setFavoritesState({
            userId: null,
            items: EMPTY_FAVORITE_ITEMS,
            error: null,
          });
          return;
        }
        if (!controller.signal.aborted) {
          setFavoritesState({
            userId: activeUserId,
            items: EMPTY_FAVORITE_ITEMS,
            error:
              error instanceof ApiError
                ? error.message
                : "No fue posible cargar tus productos favoritos.",
          });
        }
      });

    return () => controller.abort();
  }, [user?.id]);

  const reloadFavorites = useCallback(async () => {
    if (!user?.id) return;
    setReloadingFavorites(true);
    try {
      await loadFavoritesForUser(user.id);
    } finally {
      setReloadingFavorites(false);
    }
  }, [user?.id, loadFavoritesForUser]);

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
      setPendingRemoval(null);
      setFavoritesState({
        userId: null,
        items: EMPTY_FAVORITE_ITEMS,
        error: null,
      });
      router.push("/");
    }
  }, [router]);

  const favoriteItems = useMemo(
    () =>
      user && favoritesState.userId === user.id
        ? favoritesState.items
        : EMPTY_FAVORITE_ITEMS,
    [user, favoritesState]
  );

  const favoritesLoading = Boolean(
    user && (favoritesState.userId !== user.id || reloadingFavorites)
  );

  const favoritesError =
    user && favoritesState.userId === user.id ? favoritesState.error : null;

  const favorites = useMemo(
    () => favoriteItems.map((item) => item.productId),
    [favoriteItems]
  );

  const confirmRemoveFavorite = useCallback(() => {
    if (!pendingRemoval || !user) {
      setPendingRemoval(null);
      return;
    }

    const itemToRemove = pendingRemoval;
    const id = itemToRemove.productId;
    const currentUserId = user.id;

    setPendingRemoval(null);

    if (pendingIdsRef.current.has(id)) {
      return;
    }

    pendingIdsRef.current.add(id);
    setFavoritesState((prev) => ({
      userId: currentUserId,
      items:
        prev.userId === currentUserId
          ? prev.items.filter((item) => item.productId !== id)
          : EMPTY_FAVORITE_ITEMS,
      error: null,
    }));

    void removeFavorite(id)
      .catch((error) => {
        if (error instanceof ApiError && error.status === 401) {
          setUser(null);
          setFavoritesState({
            userId: null,
            items: EMPTY_FAVORITE_ITEMS,
            error: null,
          });
          return;
        }
        setFavoritesState((prev) => ({
          userId: currentUserId,
          items:
            prev.userId === currentUserId &&
            !prev.items.some((item) => item.productId === id)
              ? [itemToRemove, ...prev.items]
              : prev.items,
          error: prev.error,
        }));
      })
      .finally(() => {
        pendingIdsRef.current.delete(id);
      });
  }, [pendingRemoval, user]);

  const toggleFavorite = useCallback(
    (id: string, snapshot?: PublicProduct) => {
      if (!user) {
        requireLogin(
          "/catalogo",
          "Inicia sesión para guardar productos en tus favoritos."
        );
        return;
      }

      if (pendingIdsRef.current.has(id)) {
        return;
      }

      const currentUserId = user.id;
      const existing = favoriteItems.find((item) => item.productId === id);

      if (existing) {
        setPendingRemoval(existing);
        return;
      }

      pendingIdsRef.current.add(id);

      if (snapshot) {
        setFavoritesState((prev) => ({
          userId: currentUserId,
          items: [
            toOptimisticFavorite(snapshot),
            ...(prev.userId === currentUserId ? prev.items : []),
          ],
          error: null,
        }));
      }

      void addFavorite(id)
        .then((savedItem) => {
          setFavoritesState((prev) => ({
            userId: currentUserId,
            items: [
              savedItem,
              ...(prev.userId === currentUserId
                ? prev.items.filter((item) => item.productId !== id)
                : []),
            ],
            error: null,
          }));
        })
        .catch((error) => {
          if (error instanceof ApiError && error.status === 409) {
            void loadFavoritesForUser(currentUserId);
            return;
          }
          if (error instanceof ApiError && error.status === 401) {
            setUser(null);
            setFavoritesState({
              userId: null,
              items: EMPTY_FAVORITE_ITEMS,
              error: null,
            });
            return;
          }
          setFavoritesState((prev) => ({
            userId: currentUserId,
            items:
              prev.userId === currentUserId
                ? prev.items.filter((item) => item.productId !== id)
                : EMPTY_FAVORITE_ITEMS,
            error: prev.error,
          }));
        })
        .finally(() => {
          pendingIdsRef.current.delete(id);
        });
    },
    [user, favoriteItems, requireLogin, loadFavoritesForUser]
  );

  const value = useMemo<Store>(
    () => ({
      user,
      authLoading,
      login,
      register,
      logout,
      favorites,
      favoriteItems,
      favoritesLoading,
      favoritesError,
      reloadFavorites,
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
      favoriteItems,
      favoritesLoading,
      favoritesError,
      reloadFavorites,
      toggleFavorite,
      banners,
      query,
      requireLogin,
      loginReason,
      resetLoginIntent,
    ]
  );

  return (
    <StoreContext.Provider value={value}>
      {children}
      {pendingRemoval && (
        <ConfirmRemoveFavoriteModal
          productName={pendingRemoval.name}
          onConfirm={confirmRemoveFavorite}
          onCancel={() => setPendingRemoval(null)}
        />
      )}
    </StoreContext.Provider>
  );
}

export function useStore() {
  const ctx = useContext(StoreContext);
  if (!ctx) throw new Error("useStore debe usarse dentro de <StoreProvider>");
  return ctx;
}
