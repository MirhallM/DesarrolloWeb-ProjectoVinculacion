"use client";

import { useEffect } from "react";
import { SlidersHorizontal, X } from "@/components/ui/Icons";

interface FilterSidebarProps {
  categories: { name: string; count: number }[];
  totalCount: number;
  selected: string | null;
  onSelect: (category: string | null) => void;
  inStockOnly: boolean;
  onInStockOnlyChange: (value: boolean) => void;
  resultCount: number;
  onReset: () => void;
  /** Estado del drawer en móvil/tablet. En escritorio (`lg`+) la barra lateral es fija. */
  open: boolean;
  onClose: () => void;
}

/**
 * Barra lateral de filtros:
 * - Móvil y Tablet (< lg): Drawer deslizable accesible con soporte de tecla Escape y backdrop.
 * - Escritorio (lg+): Columna lateral izquierda fija.
 */
export function FilterSidebar({
  categories,
  totalCount,
  selected,
  onSelect,
  inStockOnly,
  onInStockOnlyChange,
  resultCount,
  onReset,
  open,
  onClose,
}: FilterSidebarProps) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    const { overflow } = document.body.style;
    document.body.style.overflow = "hidden";
    document.addEventListener("keydown", onKey);
    return () => {
      document.body.style.overflow = overflow;
      document.removeEventListener("keydown", onKey);
    };
  }, [open, onClose]);

  return (
    <>
      <div
        aria-hidden="true"
        onClick={onClose}
        className={`fixed inset-0 z-40 bg-ink/45 backdrop-blur-[2px] transition-opacity duration-300 lg:hidden ${
          open ? "opacity-100" : "pointer-events-none opacity-0"
        }`}
      />

      <aside
        id="filtros"
        aria-label="Filtros del catálogo"
        className={`fixed inset-y-0 left-0 z-50 flex w-[86%] max-w-sm flex-col bg-white shadow-2xl transition-[translate,visibility] duration-300 ease-out lg:visible lg:static lg:z-auto lg:w-auto lg:max-w-none lg:translate-x-0 lg:bg-transparent lg:shadow-none ${
          open ? "visible translate-x-0" : "invisible -translate-x-full"
        }`}
      >
        <div className="flex items-center justify-between border-b border-line px-5 py-4 lg:hidden">
          <p className="flex items-center gap-2 text-lg font-extrabold text-ink">
            <SlidersHorizontal className="size-5 text-brand-dark" />
            Filtros
          </p>
          <button
            type="button"
            onClick={onClose}
            aria-label="Cerrar panel de filtros"
            className="grid size-10 place-items-center rounded-full text-ink-soft hover:bg-canvas focus-visible:outline-2 focus-visible:outline-brand"
          >
            <X className="size-5" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto px-5 py-5 lg:sticky lg:top-28 lg:flex-none lg:overflow-visible lg:p-0">
          <p className="hidden items-center gap-2 text-xs font-bold tracking-[0.12em] text-muted uppercase lg:flex">
            <SlidersHorizontal className="size-4" />
            Categorías
          </p>
          <p className="text-xs font-bold tracking-[0.12em] text-muted uppercase lg:hidden">
            Categorías
          </p>
          <ul className="mt-3 flex flex-col gap-0.5">
            <CategoryItem
              label="Todos los productos"
              count={totalCount}
              active={!selected}
              onClick={() => onSelect(null)}
            />
            {categories.map((c) => (
              <CategoryItem
                key={c.name}
                label={c.name}
                count={c.count}
                active={selected === c.name}
                onClick={() => onSelect(c.name)}
              />
            ))}
          </ul>

          <div className="mt-6 border-t border-line pt-6">
            <p className="text-xs font-bold tracking-[0.12em] text-muted uppercase">
              Disponibilidad
            </p>
            <label className="mt-3 flex cursor-pointer items-center gap-3 rounded-lg py-1 text-sm font-medium text-ink-soft focus-within:ring-2 focus-within:ring-brand">
              <input
                type="checkbox"
                checked={inStockOnly}
                onChange={(e) => onInStockOnlyChange(e.target.checked)}
                className="size-4 rounded accent-brand focus:outline-none"
              />
              Mostrar solo disponibles
            </label>
          </div>
        </div>

        <div className="grid grid-cols-[auto_1fr] gap-3 border-t border-line p-4 lg:hidden">
          <button
            type="button"
            onClick={onReset}
            className="h-12 rounded-xl px-4 text-sm font-semibold text-ink-soft hover:bg-canvas focus-visible:outline-2 focus-visible:outline-brand"
          >
            Limpiar
          </button>
          <button
            type="button"
            onClick={onClose}
            className="h-12 rounded-xl bg-accent text-sm font-bold text-white hover:bg-accent-dark focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
          >
            Ver {resultCount} {resultCount === 1 ? "producto" : "productos"}
          </button>
        </div>
      </aside>
    </>
  );
}

function CategoryItem({
  label,
  count,
  active,
  onClick,
}: {
  label: string;
  count: number;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <li>
      <button
        type="button"
        onClick={onClick}
        aria-pressed={active}
        className={`flex w-full items-center justify-between gap-3 rounded-xl px-3 py-3 text-left text-sm font-semibold transition focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-brand-dark lg:py-2.5 ${
          active
            ? "bg-brand text-white shadow-sm"
            : "text-ink-soft hover:bg-brand-50 hover:text-brand-deep"
        }`}
      >
        <span>{label}</span>
        <span
          className={`text-xs tabular-nums ${
            active ? "text-white/85" : "text-muted"
          }`}
        >
          {count}
        </span>
      </button>
    </li>
  );
}

