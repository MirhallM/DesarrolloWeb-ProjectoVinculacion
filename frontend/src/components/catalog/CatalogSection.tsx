"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { SlidersHorizontal } from "@/components/ui/Icons";
import { ApiError } from "@/lib/api";
import { DEFAULT_PAGE_SIZE, getCategories, getProducts } from "@/lib/catalog";
import { useStore } from "@/lib/store";
import type {
  PagedResult,
  ProductSortField,
  PublicCategory,
  PublicProduct,
  SortDirection,
} from "@/types";
import {
  CatalogEmptyState,
  CatalogErrorState,
  CatalogLoadingSkeleton,
} from "./CatalogStates";
import { FilterSidebar } from "./FilterSidebar";
import { HeroBanner } from "./HeroBanner";
import { Pagination } from "./Pagination";
import { ProductCard } from "./ProductCard";

type SortOption = `${ProductSortField}-${SortDirection}`;

const SORT_OPTIONS: { value: SortOption; label: string }[] = [
  { value: "name-asc", label: "Nombre A–Z" },
  { value: "name-desc", label: "Nombre Z–A" },
  { value: "price-asc", label: "Precio: menor a mayor" },
  { value: "price-desc", label: "Precio: mayor a menor" },
];

export interface CatalogSectionProps {
  /** Página inicial obtenida en el servidor desde GET /api/products. */
  initialData: PagedResult<PublicProduct> | null;
  /** Categorías obtenidas en el servidor desde GET /api/categories. */
  initialCategories?: PublicCategory[];
  /** Mensaje de error inicial si falló la carga SSR (no rompe la página). */
  initialError?: string | null;
}

function parseSort(option: SortOption): {
  sort: ProductSortField;
  order: SortDirection;
} {
  const [sort, order] = option.split("-") as [ProductSortField, SortDirection];
  return { sort, order };
}

/**
 * Vista principal del catálogo público conectada a GET /api/products y GET /api/categories.
 * Conserva el diseño completo de Figma (HeroBanner, FilterSidebar con categorías reales, buscador y favoritos).
 */
export function CatalogSection({
  initialData,
  initialCategories = [],
  initialError = null,
}: CatalogSectionProps) {
  const {
    user,
    isFavorite,
    toggleFavorite,
    requireLogin,
    query,
    setQuery,
  } = useStore();

  const [data, setData] = useState<PagedResult<PublicProduct> | null>(
    initialData
  );
  const [categories, setCategories] =
    useState<PublicCategory[]>(initialCategories);
  const [page, setPage] = useState<number>(initialData?.page ?? 1);
  const [sortOption, setSortOption] = useState<SortOption>("name-asc");
  const [selectedCategorySlug, setSelectedCategorySlug] = useState<
    string | null
  >(null);
  const [inStockOnly, setInStockOnly] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(initialError);

  const abortRef = useRef<AbortController | null>(null);
  const sectionRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    return () => abortRef.current?.abort();
  }, []);

  const closeDrawer = useCallback(() => setDrawerOpen(false), []);

  const loadPage = useCallback(
    async (
      targetPage: number,
      targetSort: SortOption,
      scrollToTop = false
    ) => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      const { sort, order } = parseSort(targetSort);
      setLoading(true);
      setError(null);

      try {
        const [productsResult, categoriesResult] = await Promise.all([
          getProducts(
            {
              page: targetPage,
              pageSize: DEFAULT_PAGE_SIZE,
              sort,
              order,
            },
            { signal: controller.signal }
          ),
          categories.length === 0
            ? getCategories({ signal: controller.signal }).catch(() => [])
            : Promise.resolve(categories),
        ]);

        setData(productsResult);
        setPage(productsResult.page);
        if (categories.length === 0 && categoriesResult.length > 0) {
          setCategories(categoriesResult);
        }

        if (scrollToTop) {
          sectionRef.current?.scrollIntoView({
            behavior: "smooth",
            block: "start",
          });
        }
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") {
          return;
        }
        const message =
          err instanceof ApiError
            ? err.message
            : "Ocurrió un error inesperado al consultar los productos.";
        setError(message);
      } finally {
        if (abortRef.current === controller) {
          setLoading(false);
        }
      }
    },
    [categories]
  );

  const handleSortChange = (nextSort: SortOption) => {
    setSortOption(nextSort);
    void loadPage(1, nextSort);
  };

  const handlePageChange = (nextPage: number) => {
    void loadPage(nextPage, sortOption, true);
  };

  const rawItems = useMemo(() => data?.items ?? [], [data]);
  const totalCount = data?.totalCount ?? 0;
  const pageSize = data?.pageSize ?? DEFAULT_PAGE_SIZE;

  const filteredItems = useMemo(() => {
    const q = query.trim().toLowerCase();
    return rawItems.filter(
      (p) =>
        (!selectedCategorySlug || p.category.slug === selectedCategorySlug) &&
        (!inStockOnly || p.available) &&
        (!q || p.name.toLowerCase().includes(q))
    );
  }, [rawItems, selectedCategorySlug, inStockOnly, query]);

  const selectedCategoryName = useMemo(
    () =>
      categories.find((c) => c.slug === selectedCategorySlug)?.name ?? null,
    [categories, selectedCategorySlug]
  );

  const activeFilters =
    Number(Boolean(selectedCategorySlug)) + Number(inStockOnly);
  const hasAnyFilterOrQuery = Boolean(query.trim()) || activeFilters > 0;

  const resetAllFilters = () => {
    setQuery("");
    setSelectedCategorySlug(null);
    setInStockOnly(false);
  };

  const onHeart = (product: PublicProduct) =>
    user
      ? toggleFavorite(product.id, product)
      : requireLogin(
          "/catalogo",
          "Inicia sesión para guardar productos en tus favoritos."
        );

  return (
    <main className="mx-auto max-w-7xl px-4 pt-5 pb-20 sm:px-6 sm:pt-6 lg:px-8">
      <HeroBanner />

      <div
        id="catalogo-productos"
        className="mt-8 lg:mt-10 lg:grid lg:grid-cols-[240px_1fr] lg:gap-8"
      >
        <FilterSidebar
          categories={categories}
          totalCount={totalCount}
          selectedSlug={selectedCategorySlug}
          onSelect={setSelectedCategorySlug}
          inStockOnly={inStockOnly}
          onInStockOnlyChange={setInStockOnly}
          resultCount={filteredItems.length}
          onReset={() => {
            setSelectedCategorySlug(null);
            setInStockOnly(false);
          }}
          open={drawerOpen}
          onClose={closeDrawer}
        />

        <section
          ref={sectionRef}
          aria-labelledby="grid-title"
          className="min-w-0"
        >
          <div className="flex flex-wrap items-end justify-between gap-3 border-b border-line pb-4">
            <div className="min-w-0">
              <h2
                id="grid-title"
                className="truncate text-2xl font-extrabold tracking-tight text-ink"
              >
                {query
                  ? `Resultados para “${query}”`
                  : (selectedCategoryName ?? "Todos los productos")}
              </h2>
              <p className="mt-1 text-sm text-muted">
                {loading
                  ? "Consultando catálogo..."
                  : error
                    ? "No fue posible obtener el catálogo."
                    : hasAnyFilterOrQuery
                      ? `${filteredItems.length} ${
                          filteredItems.length === 1 ? "producto" : "productos"
                        }`
                      : `${totalCount} ${
                          totalCount === 1 ? "producto" : "productos"
                        }`}
              </p>
            </div>

            <div className="flex w-full flex-wrap items-center gap-2 sm:w-auto">
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
                  value={sortOption}
                  onChange={(e) =>
                    handleSortChange(e.target.value as SortOption)
                  }
                  aria-label="Ordenar productos por"
                  disabled={loading}
                  className="h-10 rounded-xl border border-line bg-white px-3 text-sm font-semibold text-ink outline-none focus:border-brand focus:ring-2 focus:ring-brand/30 disabled:opacity-50"
                >
                  {SORT_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
              </label>
            </div>
          </div>

          {loading ? (
            <CatalogLoadingSkeleton count={8} />
          ) : error ? (
            <CatalogErrorState
              message={error}
              onRetry={() => void loadPage(page, sortOption)}
            />
          ) : filteredItems.length > 0 ? (
            <>
              <div className="mt-6 grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
                {filteredItems.map((product, index) => (
                  <ProductCard
                    key={product.id}
                    product={product}
                    eager={index < 4}
                    favorite={Boolean(user && isFavorite(product.id))}
                    onToggleFavorite={() => onHeart(product)}
                    sizes="(min-width: 1280px) 22vw, (min-width: 1024px) 30vw, (min-width: 640px) 50vw, 100vw"
                  />
                ))}
              </div>

              <Pagination
                page={page}
                pageSize={pageSize}
                totalCount={totalCount}
                disabled={loading}
                onPageChange={handlePageChange}
              />
            </>
          ) : (
            <CatalogEmptyState
              hasActiveFilters={hasAnyFilterOrQuery}
              onResetFilters={resetAllFilters}
            />
          )}
        </section>
      </div>
    </main>
  );
}
