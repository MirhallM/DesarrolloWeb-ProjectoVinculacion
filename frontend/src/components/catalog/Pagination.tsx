"use client";

import { ChevronLeft, ChevronRight } from "@/components/ui/Icons";

interface PaginationProps {
  page: number;
  pageSize: number;
  totalCount: number;
  disabled?: boolean;
  onPageChange: (nextPage: number) => void;
}

function getVisiblePages(current: number, totalPages: number): (number | "...")[] {
  if (totalPages <= 7) {
    return Array.from({ length: totalPages }, (_, i) => i + 1);
  }

  const pages: (number | "...")[] = [1];
  const start = Math.max(2, current - 1);
  const end = Math.min(totalPages - 1, current + 1);

  if (start > 2) pages.push("...");
  for (let i = start; i <= end; i++) {
    pages.push(i);
  }
  if (end < totalPages - 1) pages.push("...");
  pages.push(totalPages);

  return pages;
}

export function Pagination({
  page,
  pageSize,
  totalCount,
  disabled = false,
  onPageChange,
}: PaginationProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  if (totalPages <= 1) return null;

  const visiblePages = getVisiblePages(page, totalPages);

  return (
    <nav
      aria-label="Paginación del catálogo"
      className="mt-10 flex flex-wrap items-center justify-between gap-4 border-t border-line pt-6"
    >
      <p className="text-sm text-muted">
        Página <span className="font-semibold text-ink">{page}</span> de{" "}
        <span className="font-semibold text-ink">{totalPages}</span>
      </p>

      <div className="flex flex-wrap items-center gap-1.5">
        <button
          type="button"
          disabled={disabled || page <= 1}
          onClick={() => onPageChange(page - 1)}
          aria-label="Página anterior"
          className="inline-flex h-10 items-center gap-1.5 rounded-xl border border-line bg-white px-3.5 text-sm font-semibold text-ink transition hover:border-brand disabled:cursor-not-allowed disabled:opacity-45 focus-visible:outline-2 focus-visible:outline-brand"
        >
          <ChevronLeft className="size-4" />
          <span className="hidden sm:inline">Anterior</span>
        </button>

        {visiblePages.map((entry, idx) =>
          entry === "..." ? (
            <span
              key={`ellipsis-${idx}`}
              aria-hidden="true"
              className="px-2 text-sm text-muted"
            >
              …
            </span>
          ) : (
            <button
              key={entry}
              type="button"
              disabled={disabled}
              onClick={() => onPageChange(entry)}
              aria-current={entry === page ? "page" : undefined}
              aria-label={`Ir a página ${entry}`}
              className={`grid h-10 min-w-10 place-items-center rounded-xl px-3 text-sm font-semibold transition focus-visible:outline-2 focus-visible:outline-brand ${
                entry === page
                  ? "bg-brand text-white shadow-xs"
                  : "border border-line bg-white text-ink hover:border-brand"
              }`}
            >
              {entry}
            </button>
          )
        )}

        <button
          type="button"
          disabled={disabled || page >= totalPages}
          onClick={() => onPageChange(page + 1)}
          aria-label="Página siguiente"
          className="inline-flex h-10 items-center gap-1.5 rounded-xl border border-line bg-white px-3.5 text-sm font-semibold text-ink transition hover:border-brand disabled:cursor-not-allowed disabled:opacity-45 focus-visible:outline-2 focus-visible:outline-brand"
        >
          <span className="hidden sm:inline">Siguiente</span>
          <ChevronRight className="size-4" />
        </button>
      </div>
    </nav>
  );
}
