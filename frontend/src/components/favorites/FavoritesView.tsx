"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { ProductCard } from "@/components/catalog/ProductCard";
import { Heart, Lock, Search, X } from "@/components/ui/Icons";
import { useStore } from "@/lib/store";
import type { FavoriteAvailabilityStatus } from "@/types";

type StatusFilter = "all" | FavoriteAvailabilityStatus;
type FavoritesSort = "recent" | "name-asc" | "price-asc" | "price-desc";

export function FavoritesView() {
  const {
    authLoading,
    user,
    favoriteItems,
    favoritesLoading,
    favoritesError,
    reloadFavorites,
    toggleFavorite,
    requireLogin,
  } = useStore();

  const [searchText, setSearchText] = useState("");
  const [categorySlug, setCategorySlug] = useState<string>("all");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [sortBy, setSortBy] = useState<FavoritesSort>("recent");

  const categoryOptions = useMemo(() => {
    const map = new Map<string, { slug: string; name: string; count: number }>();
    for (const item of favoriteItems) {
      const prev = map.get(item.category.slug);
      if (prev) {
        prev.count += 1;
      } else {
        map.set(item.category.slug, {
          slug: item.category.slug,
          name: item.category.name,
          count: 1,
        });
      }
    }
    return Array.from(map.values()).sort((a, b) =>
      a.name.localeCompare(b.name, "es")
    );
  }, [favoriteItems]);

  const counts = useMemo(() => {
    let available = 0;
    let outOfStock = 0;
    let unavailable = 0;

    for (const item of favoriteItems) {
      if (!item.isVisible || item.availabilityStatus === "unavailable") {
        unavailable += 1;
      } else if (item.available) {
        available += 1;
      } else {
        outOfStock += 1;
      }
    }

    return { available, outOfStock, unavailable };
  }, [favoriteItems]);

  const effectiveCategorySlug =
    categorySlug !== "all" &&
    !categoryOptions.some((c) => c.slug === categorySlug)
      ? "all"
      : categorySlug;

  const filteredItems = useMemo(() => {
    const normalizedQuery = searchText.trim().toLowerCase();

    const filtered = favoriteItems.filter((item) => {
      const itemStatus: FavoriteAvailabilityStatus = !item.isVisible
        ? "unavailable"
        : item.availabilityStatus;

      if (statusFilter !== "all" && itemStatus !== statusFilter) {
        return false;
      }

      if (
        effectiveCategorySlug !== "all" &&
        item.category.slug !== effectiveCategorySlug
      ) {
        return false;
      }

      if (
        normalizedQuery &&
        !item.name.toLowerCase().includes(normalizedQuery)
      ) {
        return false;
      }

      return true;
    });

    return [...filtered].sort((a, b) => {
      if (sortBy === "name-asc") {
        return a.name.localeCompare(b.name, "es");
      }
      if (sortBy === "price-asc") {
        const pa = a.price ?? Number.POSITIVE_INFINITY;
        const pb = b.price ?? Number.POSITIVE_INFINITY;
        return pa - pb || a.name.localeCompare(b.name, "es");
      }
      if (sortBy === "price-desc") {
        const pa = a.price ?? Number.NEGATIVE_INFINITY;
        const pb = b.price ?? Number.NEGATIVE_INFINITY;
        return pb - pa || a.name.localeCompare(b.name, "es");
      }
      return b.savedAt.localeCompare(a.savedAt);
    });
  }, [
    favoriteItems,
    searchText,
    statusFilter,
    effectiveCategorySlug,
    sortBy,
  ]);

  const hasActiveFilters =
    Boolean(searchText.trim()) ||
    statusFilter !== "all" ||
    effectiveCategorySlug !== "all";

  const clearFilters = () => {
    setSearchText("");
    setCategorySlug("all");
    setStatusFilter("all");
    setSortBy("recent");
  };

  if (authLoading || (user && favoritesLoading && favoriteItems.length === 0)) {
    return (
      <main
        className="mx-auto max-w-7xl px-4 pt-10 pb-20 sm:px-6 lg:px-8"
        aria-busy="true"
      >
        <div className="h-10 w-56 animate-pulse rounded-xl bg-canvas" />
        <div className="mt-8 grid grid-cols-1 gap-5 md:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }, (_, i) => (
            <div
              key={i}
              className="aspect-[4/5] animate-pulse rounded-2xl bg-canvas"
            />
          ))}
        </div>
      </main>
    );
  }

  if (!user) {
    return (
      <main className="mx-auto flex max-w-7xl flex-col items-center px-4 py-24 text-center sm:px-6 lg:px-8">
        <div className="grid size-16 place-items-center rounded-full bg-brand-50 text-brand-dark">
          <Lock className="size-7" />
        </div>
        <h1 className="mt-5 text-2xl font-extrabold tracking-tight text-ink">
          Mis Favoritos es un área privada
        </h1>
        <p className="mt-2 max-w-sm text-sm text-muted">
          Inicia sesión para ver y administrar los productos que has guardado.
        </p>
        <button
          type="button"
          onClick={() =>
            requireLogin(
              "/favorites",
              "Inicia sesión para ver tus productos guardados."
            )
          }
          className="mt-6 rounded-full bg-accent px-6 py-3 text-sm font-bold text-white transition hover:bg-accent-dark focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
        >
          Iniciar sesión
        </button>
      </main>
    );
  }

  const statusChips: { value: StatusFilter; label: string; count: number; show: boolean }[] = [
    { value: "all", label: "Todos", count: favoriteItems.length, show: true },
    { value: "available", label: "Disponibles", count: counts.available, show: true },
    { value: "out_of_stock", label: "Agotados", count: counts.outOfStock, show: true },
    {
      value: "unavailable",
      label: "No disponibles",
      count: counts.unavailable,
      show: counts.unavailable > 0 || statusFilter === "unavailable",
    },
  ];

  return (
    <main className="mx-auto max-w-7xl px-4 pt-8 pb-20 sm:px-6 sm:pt-10 lg:px-8">
      <div className="flex flex-wrap items-end justify-between gap-4 border-b border-line pb-6">
        <div>
          <p className="text-sm font-semibold text-brand-dark">
            Hola, {user.name}
          </p>
          <h1 className="mt-1 text-3xl font-extrabold tracking-tight text-ink sm:text-4xl">
            Mis Favoritos
          </h1>
          <p className="mt-2 text-sm text-muted">
            {hasActiveFilters
              ? `Mostrando ${filteredItems.length} de ${favoriteItems.length} ${
                  favoriteItems.length === 1
                    ? "producto guardado"
                    : "productos guardados"
                }`
              : `${favoriteItems.length} ${
                  favoriteItems.length === 1
                    ? "producto guardado"
                    : "productos guardados"
                }`}
          </p>
        </div>
        <Link
          href="/catalogo"
          className="rounded-full border border-line px-5 py-2.5 text-sm font-semibold text-ink-soft transition hover:border-brand hover:text-brand-deep focus-visible:outline-2 focus-visible:outline-brand"
        >
          Seguir comprando
        </Link>
      </div>

      {favoritesError ? (
        <div
          role="alert"
          className="mt-10 flex flex-col items-center rounded-3xl bg-red-50 px-6 py-14 text-center ring-1 ring-red-200"
        >
          <p className="text-base font-bold text-red-800">
            No se pudieron cargar tus favoritos
          </p>
          <p className="mt-1.5 max-w-md text-sm text-red-700">
            {favoritesError}
          </p>
          <button
            type="button"
            onClick={() => void reloadFavorites()}
            className="mt-5 rounded-full bg-accent px-5 py-2.5 text-sm font-bold text-white transition hover:bg-accent-dark focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
          >
            Reintentar
          </button>
        </div>
      ) : favoriteItems.length > 0 ? (
        <>
          <section
            aria-label="Filtros de mis favoritos"
            className="mt-6 flex flex-col gap-3 rounded-2xl bg-canvas/70 p-3.5 ring-1 ring-line sm:p-4"
          >
            <div className="flex flex-col gap-3 lg:flex-row lg:items-center">
              <div className="relative flex-1">
                <Search className="pointer-events-none absolute top-1/2 left-3.5 size-4 -translate-y-1/2 text-muted" />
                <input
                  type="search"
                  value={searchText}
                  onChange={(e) => setSearchText(e.target.value)}
                  placeholder="Buscar en mis favoritos..."
                  aria-label="Buscar en mis favoritos"
                  className="h-10 w-full rounded-xl border border-line bg-white pr-9 pl-10 text-sm text-ink outline-none transition placeholder:text-muted focus:border-brand focus:ring-4 focus:ring-brand/15 [&::-webkit-search-cancel-button]:hidden"
                />
                {searchText && (
                  <button
                    type="button"
                    onClick={() => setSearchText("")}
                    aria-label="Limpiar búsqueda"
                    className="absolute top-1/2 right-2 grid size-6 -translate-y-1/2 place-items-center rounded-full text-muted hover:bg-canvas hover:text-ink"
                  >
                    <X className="size-3.5" />
                  </button>
                )}
              </div>

              <div className="flex flex-wrap items-center gap-2.5">
                <select
                  value={effectiveCategorySlug}
                  onChange={(e) => setCategorySlug(e.target.value)}
                  aria-label="Filtrar favoritos por categoría"
                  className="h-10 rounded-xl border border-line bg-white px-3.5 text-sm font-medium text-ink outline-none transition focus:border-brand focus:ring-4 focus:ring-brand/15"
                >
                  <option value="all">Todas las categorías</option>
                  {categoryOptions.map((cat) => (
                    <option key={cat.slug} value={cat.slug}>
                      {cat.name} ({cat.count})
                    </option>
                  ))}
                </select>

                <select
                  value={sortBy}
                  onChange={(e) => setSortBy(e.target.value as FavoritesSort)}
                  aria-label="Ordenar favoritos"
                  className="h-10 rounded-xl border border-line bg-white px-3.5 text-sm font-medium text-ink outline-none transition focus:border-brand focus:ring-4 focus:ring-brand/15"
                >
                  <option value="recent">Guardados recientemente</option>
                  <option value="name-asc">Nombre A–Z</option>
                  <option value="price-asc">Precio: menor a mayor</option>
                  <option value="price-desc">Precio: mayor a menor</option>
                </select>
              </div>
            </div>

            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="flex flex-wrap gap-1.5">
                {statusChips
                  .filter((chip) => chip.show)
                  .map((chip) => {
                    const active = statusFilter === chip.value;
                    return (
                      <button
                        key={chip.value}
                        type="button"
                        onClick={() => setStatusFilter(chip.value)}
                        aria-pressed={active}
                        className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-semibold transition focus-visible:outline-2 focus-visible:outline-brand ${
                          active
                            ? "bg-brand text-white shadow-xs"
                            : "bg-white text-ink-soft ring-1 ring-line hover:text-ink"
                        }`}
                      >
                        {chip.label}
                        <span
                          className={`rounded-full px-1.5 py-0.2 text-[11px] tabular-nums ${
                            active
                              ? "bg-white/20 text-white"
                              : "bg-canvas text-muted"
                          }`}
                        >
                          {chip.count}
                        </span>
                      </button>
                    );
                  })}
              </div>

              {hasActiveFilters && (
                <button
                  type="button"
                  onClick={clearFilters}
                  className="text-xs font-semibold text-brand-dark underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-brand"
                >
                  Limpiar filtros
                </button>
              )}
            </div>
          </section>

          {filteredItems.length > 0 ? (
            <div className="mt-7 grid grid-cols-1 gap-5 md:grid-cols-2 lg:grid-cols-4">
              {filteredItems.map((item, i) => (
                <ProductCard
                  key={item.productId}
                  eager={i < 4}
                  product={{
                    id: item.productId,
                    name: item.name,
                    price: item.price,
                    available: item.available,
                    category: item.category,
                    imageUrl: item.imageUrl,
                  }}
                  favorite
                  variant="favorites"
                  unavailable={
                    !item.isVisible || item.availabilityStatus === "unavailable"
                  }
                  onToggleFavorite={() => toggleFavorite(item.productId)}
                  sizes="(min-width: 1024px) 25vw, (min-width: 768px) 50vw, 100vw"
                />
              ))}
            </div>
          ) : (
            <div className="mt-8 flex flex-col items-center rounded-3xl bg-canvas px-6 py-16 text-center ring-1 ring-line">
              <p className="text-lg font-bold text-ink">
                Ningún favorito coincide con tu filtro
              </p>
              <p className="mt-1.5 max-w-sm text-sm text-muted">
                Prueba con otro término, categoría o estado de disponibilidad.
              </p>
              <button
                type="button"
                onClick={clearFilters}
                className="mt-5 rounded-full bg-brand px-5 py-2.5 text-sm font-bold text-white transition hover:bg-brand-dark focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand"
              >
                Ver todos mis favoritos
              </button>
            </div>
          )}
        </>
      ) : (
        <div className="mt-10 flex flex-col items-center rounded-3xl bg-brand-50 px-6 py-20 text-center">
          <div className="grid size-16 place-items-center rounded-full bg-white text-accent shadow-sm">
            <Heart className="size-7" />
          </div>
          <p className="mt-5 text-xl font-bold text-ink">
            Aún no tienes favoritos
          </p>
          <p className="mt-1.5 max-w-sm text-sm text-ink-soft">
            Toca el corazón de cualquier producto para guardarlo aquí para tu
            próxima visita.
          </p>
          <Link
            href="/catalogo"
            className="mt-6 rounded-full bg-accent px-6 py-3 text-sm font-bold text-white transition hover:bg-accent-dark focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
          >
            Explorar el catálogo
          </Link>
        </div>
      )}
    </main>
  );
}
