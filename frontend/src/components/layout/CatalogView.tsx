"use client";

import { useCallback, useMemo, useState } from "react";
import { SlidersHorizontal } from "@/components/ui/Icons";
import {
  CatalogEmptyState,
  CatalogErrorState,
  CatalogLoadingSkeleton,
} from "@/components/ui/CatalogStates";
import { ProductCard } from "@/components/ui/ProductCard";
import { CATEGORIES, PRODUCTS } from "@/lib/data";
import { useStore } from "@/lib/store";
import type { PublicProduct } from "@/types";
import { FilterSidebar } from "./FilterSidebar";
import { HeroBanner } from "./HeroBanner";

type Sort = "featured" | "price-asc" | "price-desc" | "name";
type ViewState = "ready" | "loading" | "error" | "empty";

export interface CatalogViewProps {
  /** Lista inicial de productos obtenida desde el Server Component. */
  initialProducts?: PublicProduct[];
  /** Estado inicial opcional (por ejemplo si el servidor reporta error). */
  initialState?: ViewState;
  /** Mostrar el banner promocional en la parte superior. */
  showHero?: boolean;
}

export function CatalogView({
  initialProducts = PRODUCTS,
  initialState = "ready",
  showHero = true,
}: CatalogViewProps) {
  const {
    user,
    isFavorite,
    toggleFavorite,
    requireLogin,
    featured,
    query,
    setQuery,
  } = useStore();

  const [category, setCategory] = useState<string | null>(null);
  const [inStockOnly, setInStockOnly] = useState(false);
  const [sort, setSort] = useState<Sort>("featured");
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [viewState, setViewState] = useState<ViewState>(initialState);

  const closeDrawer = useCallback(() => setDrawerOpen(false), []);

  const productsSource = useMemo(
    () => (viewState === "empty" ? [] : initialProducts),
    [viewState, initialProducts]
  );

  const categories = useMemo(
    () =>
      CATEGORIES.map((name) => ({
        name,
        count: productsSource.filter((p) => p.category === name).length,
      })),
    [productsSource]
  );

  /**
   * Filtrado y ordenamiento del catálogo público:
   * Según docs/business-rules.md (Sección 7 y US-11), la búsqueda pública
   * se realiza únicamente por el nombre para mostrar (displayName), nunca por código.
   */
  const results = useMemo(() => {
    const q = query.trim().toLowerCase();
    const list = productsSource.filter(
      (p) =>
        (!category || p.category === category) &&
        (!inStockOnly || p.availability === "available") &&
        (!q || p.displayName.toLowerCase().includes(q))
    );

    return [...list].sort((a, b) => {
      if (sort === "price-asc") return a.publicPrice - b.publicPrice;
      if (sort === "price-desc") return b.publicPrice - a.publicPrice;
      if (sort === "name")
        return a.displayName.localeCompare(b.displayName, "es");
      const aFeat = Number(Boolean(featured[a.id] ?? a.isFeatured));
      const bFeat = Number(Boolean(featured[b.id] ?? b.isFeatured));
      return bFeat - aFeat;
    });
  }, [productsSource, query, category, inStockOnly, sort, featured]);

  const activeFilters = Number(Boolean(category)) + Number(inStockOnly);
  const hasAnyFilterOrQuery = Boolean(query.trim()) || activeFilters > 0;

  const resetAll = () => {
    setQuery("");
    setCategory(null);
    setInStockOnly(false);
  };

  const onHeart = (id: string) =>
    user
      ? toggleFavorite(id)
      : requireLogin(
          "/catalogo",
          "Inicia sesión para guardar productos en tus favoritos."
        );

  return (
    <main className="mx-auto max-w-7xl px-4 pt-5 pb-20 sm:px-6 sm:pt-6 lg:px-8">
      {showHero && <HeroBanner />}

      <div
        id="catalogo-productos"
        className="mt-8 lg:mt-10 lg:grid lg:grid-cols-[240px_1fr] lg:gap-8"
      >
        <FilterSidebar
          categories={categories}
          totalCount={productsSource.length}
          selected={category}
          onSelect={setCategory}
          inStockOnly={inStockOnly}
          onInStockOnlyChange={setInStockOnly}
          resultCount={results.length}
          onReset={() => {
            setCategory(null);
            setInStockOnly(false);
          }}
          open={drawerOpen}
          onClose={closeDrawer}
        />

        <section aria-labelledby="grid-title" className="min-w-0">
          <div className="flex flex-wrap items-end justify-between gap-3 border-b border-line pb-4">
            <div className="min-w-0">
              <h2
                id="grid-title"
                className="truncate text-2xl font-extrabold tracking-tight text-ink"
              >
                {query
                  ? `Resultados para “${query}”`
                  : (category ?? "Todos los productos")}
              </h2>
              <p className="mt-1 text-sm text-muted">
                {viewState === "loading"
                  ? "Consultando catálogo..."
                  : `${results.length} ${
                      results.length === 1 ? "producto" : "productos"
                    }`}
              </p>
            </div>

            <div className="flex w-full flex-wrap items-center gap-2 sm:w-auto">
              {/* Botón de apertura de filtros en móvil y tablet */}
              <button
                type="button"
                onClick={() => setDrawerOpen(true)}
                aria-controls="filtros"
                aria-expanded={drawerOpen}
                className="inline-flex h-10 items-center gap-2 rounded-xl border border-line bg-white px-3.5 text-sm font-semibold text-ink hover:border-brand focus-visible:outline-2 focus-visible:outline-brand lg:hidden"
              >
                <SlidersHorizontal className="size-4" />
                Filtros
                {activeFilters > 0 && (
                  <span className="grid size-5 place-items-center rounded-full bg-brand text-[11px] font-bold text-white">
                    {activeFilters}
                  </span>
                )}
              </button>

              <label className="ml-auto flex items-center gap-2 text-sm text-muted">
                <span className="hidden sm:inline">Ordenar por</span>
                <select
                  value={sort}
                  onChange={(e) => setSort(e.target.value as Sort)}
                  aria-label="Ordenar productos por"
                  disabled={viewState !== "ready"}
                  className="h-10 rounded-xl border border-line bg-white px-3 text-sm font-semibold text-ink outline-none focus:border-brand focus:ring-2 focus:ring-brand/30 disabled:opacity-50"
                >
                  <option value="featured">Destacados</option>
                  <option value="price-asc">Precio: menor a mayor</option>
                  <option value="price-desc">Precio: mayor a menor</option>
                  <option value="name">Nombre A–Z</option>
                </select>
              </label>
            </div>
          </div>

          {/* Barra de previsualización de estados de la interfaz (Carga, Error, Vacío, Normal) */}
          <div
            role="group"
            aria-label="Previsualización de estados del catálogo"
            className="mt-4 flex flex-wrap items-center gap-2 rounded-2xl bg-canvas/80 px-3.5 py-2 text-xs text-muted"
          >
            <span className="font-semibold text-ink-soft">
              Estado de vista:
            </span>
            {(
              [
                { id: "ready", label: "Con productos" },
                { id: "loading", label: "Cargando" },
                { id: "error", label: "Error" },
                { id: "empty", label: "Catálogo vacío" },
              ] as const
            ).map((st) => (
              <button
                key={st.id}
                type="button"
                onClick={() => setViewState(st.id)}
                aria-pressed={viewState === st.id}
                className={`rounded-full px-2.5 py-1 font-semibold transition focus-visible:outline-2 focus-visible:outline-brand ${
                  viewState === st.id
                    ? "bg-brand text-white shadow-2xs"
                    : "bg-white text-ink-soft ring-1 ring-line hover:text-ink"
                }`}
              >
                {st.label}
              </button>
            ))}
          </div>

          {/* Renderizado condicional según el estado del catálogo */}
          {viewState === "loading" ? (
            <CatalogLoadingSkeleton count={8} />
          ) : viewState === "error" ? (
            <CatalogErrorState onRetry={() => setViewState("ready")} />
          ) : results.length > 0 ? (
            <div className="mt-6 grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
              {results.map((product, i) => (
                <ProductCard
                  key={product.id}
                  eager={i < 4}
                  product={product}
                  featured={Boolean(
                    featured[product.id] ?? product.isFeatured
                  )}
                  favorite={Boolean(user && isFavorite(product.id))}
                  onToggleFavorite={() => onHeart(product.id)}
                  sizes="(min-width: 1280px) 22vw, (min-width: 1024px) 30vw, (min-width: 640px) 50vw, 100vw"
                />
              ))}
            </div>
          ) : (
            <CatalogEmptyState
              hasActiveFilters={hasAnyFilterOrQuery}
              onResetFilters={() => {
                resetAll();
                if (viewState === "empty") setViewState("ready");
              }}
            />
          )}
        </section>
      </div>
    </main>
  );
}

