"use client";

interface CatalogLoadingSkeletonProps {
  count?: number;
}

/**
 * Estado de carga (Skeleton) para el grid del catálogo público.
 */
export function CatalogLoadingSkeleton({
  count = 8,
}: CatalogLoadingSkeletonProps) {
  return (
    <div
      role="status"
      aria-live="polite"
      aria-busy="true"
      aria-label="Cargando productos del catálogo"
      className="mt-6 grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4"
    >
      {Array.from({ length: count }).map((_, index) => (
        <div
          key={index}
          className="flex flex-col overflow-hidden rounded-2xl bg-white ring-1 ring-line"
        >
          <div className="aspect-[4/3] w-full animate-pulse bg-canvas" />
          <div className="flex flex-1 flex-col p-4">
            <div className="h-4 w-24 animate-pulse rounded-md bg-canvas" />
            <div className="mt-2.5 h-5 w-4/5 animate-pulse rounded-md bg-canvas" />
            <div className="mt-1.5 h-5 w-2/3 animate-pulse rounded-md bg-canvas" />
            <div className="mt-auto pt-4">
              <div className="h-7 w-28 animate-pulse rounded-md bg-canvas" />
            </div>
            <div className="mt-3">
              <div className="h-6 w-24 animate-pulse rounded-full bg-canvas" />
            </div>
          </div>
        </div>
      ))}
      <span className="sr-only">Cargando catálogo de productos...</span>
    </div>
  );
}

interface CatalogErrorStateProps {
  message?: string;
  onRetry?: () => void;
}

/**
 * Estado de error cuando falla la comunicación con el backend.
 */
export function CatalogErrorState({
  message = "No pudimos cargar los productos en este momento. Verifica tu conexión o intenta de nuevo.",
  onRetry,
}: CatalogErrorStateProps) {
  return (
    <div
      role="alert"
      className="mt-6 flex flex-col items-center rounded-3xl border border-red-200 bg-red-50/50 px-6 py-16 text-center"
    >
      <div className="grid size-14 place-items-center rounded-full bg-red-100 text-red-600">
        <svg
          aria-hidden="true"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth={2}
          strokeLinecap="round"
          strokeLinejoin="round"
          className="size-6"
        >
          <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3" />
          <path d="M12 9v4" />
          <path d="M12 17h.01" />
        </svg>
      </div>
      <p className="mt-4 text-lg font-bold text-ink">
        Error al cargar el catálogo
      </p>
      <p className="mt-1 max-w-md text-sm text-ink-soft">{message}</p>
      {onRetry && (
        <button
          type="button"
          onClick={onRetry}
          className="mt-5 inline-flex items-center gap-2 rounded-full bg-brand-dark px-5 py-2.5 text-sm font-semibold text-white transition hover:bg-brand-deep focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-dark"
        >
          <svg
            aria-hidden="true"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth={2}
            strokeLinecap="round"
            strokeLinejoin="round"
            className="size-4"
          >
            <path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8" />
            <path d="M21 3v5h-5" />
            <path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16" />
            <path d="M8 16H3v5" />
          </svg>
          Reintentar
        </button>
      )}
    </div>
  );
}

interface CatalogEmptyStateProps {
  hasActiveFilters: boolean;
  onResetFilters?: () => void;
}

/**
 * Estado vacío cuando no hay coincidencias con los filtros o el catálogo aún no tiene productos activos.
 */
export function CatalogEmptyState({
  hasActiveFilters,
  onResetFilters,
}: CatalogEmptyStateProps) {
  return (
    <div
      role="status"
      aria-live="polite"
      className="mt-6 flex flex-col items-center rounded-3xl border border-dashed border-line px-6 py-16 text-center"
    >
      <div className="grid size-14 place-items-center rounded-full bg-canvas text-muted">
        <svg
          aria-hidden="true"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth={2}
          strokeLinecap="round"
          strokeLinejoin="round"
          className="size-6"
        >
          <path d="m13.5 8.5-5 5" />
          <path d="m8.5 8.5 5 5" />
          <circle cx="11" cy="11" r="8" />
          <path d="m21 21-4.3-4.3" />
        </svg>
      </div>
      <p className="mt-4 text-lg font-bold text-ink">
        {hasActiveFilters
          ? "No encontramos productos"
          : "Catálogo sin productos disponibles"}
      </p>
      <p className="mt-1 max-w-sm text-sm text-muted">
        {hasActiveFilters
          ? "Prueba buscando con otro nombre o limpia los filtros seleccionados."
          : "En este momento no hay productos publicados en el catálogo. Vuelve a consultar pronto."}
      </p>
      {hasActiveFilters && onResetFilters && (
        <button
          type="button"
          onClick={onResetFilters}
          className="mt-5 rounded-full bg-ink px-5 py-2.5 text-sm font-semibold text-white transition hover:bg-ink-soft focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ink"
        >
          Limpiar búsqueda y filtros
        </button>
      )}
    </div>
  );
}

