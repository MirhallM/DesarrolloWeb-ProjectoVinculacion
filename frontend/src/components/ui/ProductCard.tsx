"use client";

import { useState } from "react";
import Image from "next/image";
import { formatPrice } from "@/lib/utils";
import type { ProductAvailability, PublicProduct } from "@/types";
import { ProductImagePlaceholder } from "./ProductImagePlaceholder";

const STATUS: Record<
  ProductAvailability,
  { label: string; dot: string; badge: string }
> = {
  available: {
    label: "Disponible",
    dot: "bg-brand",
    badge: "bg-brand-50 text-brand-deep ring-brand/25",
  },
  out_of_stock: {
    label: "Agotado",
    dot: "bg-red-500",
    badge: "bg-red-50 text-red-700 ring-red-200",
  },
};

export interface ProductCardProps {
  product: PublicProduct;
  favorite: boolean;
  featured?: boolean;
  onToggleFavorite: () => void;
  variant?: "catalog" | "favorites";
  /** `sizes` para optimización responsive de la imagen según las columnas del grid. */
  sizes?: string;
  /** Carga inmediata para las tarjetas de la primera fila visible. */
  eager?: boolean;
}

/**
 * Tarjeta del catálogo público (US-10).
 * Muestra estrictamente los campos públicos autorizados en docs/business-rules.md (Sección 7):
 * - Nombre para mostrar (displayName)
 * - Precio público (publicPrice)
 * - Estado Disponible / Agotado (availability)
 * - Categoría (category)
 * - Imagen o imagen genérica (imageUrl)
 * No expone el código de artículo (CVE_ART) ni la cantidad en existencia (EXIST).
 */
export function ProductCard({
  product,
  favorite,
  featured,
  onToggleFavorite,
  variant = "catalog",
  sizes = "(min-width: 1280px) 22vw, (min-width: 1024px) 30vw, (min-width: 768px) 50vw, 100vw",
  eager = false,
}: ProductCardProps) {
  const [imageError, setImageError] = useState(false);
  const isOutOfStock = product.availability === "out_of_stock";
  const status = STATUS[product.availability];
  const filled = favorite || variant === "favorites";
  const showRealImage = Boolean(product.imageUrl) && !imageError;

  return (
    <article
      aria-label={`${product.displayName} - ${status.label}`}
      className={`group relative flex flex-col overflow-hidden rounded-2xl ring-1 transition duration-200 focus-within:ring-2 focus-within:ring-brand ${
        isOutOfStock
          ? "bg-canvas/45 ring-line/90"
          : "bg-white ring-line hover:-translate-y-0.5 hover:shadow-[0_12px_32px_-12px_rgba(31,36,33,0.25)] hover:ring-brand/40"
      }`}
    >
      <div className="relative aspect-[4/3] overflow-hidden bg-canvas">
        {showRealImage && product.imageUrl ? (
          <Image
            src={product.imageUrl}
            alt={product.imageAlt || product.displayName}
            fill
            sizes={sizes}
            loading={eager ? "eager" : "lazy"}
            onError={() => setImageError(true)}
            className={`object-cover transition duration-500 group-hover:scale-105 ${
              isOutOfStock ? "opacity-55 grayscale-[50%]" : ""
            }`}
          />
        ) : (
          <ProductImagePlaceholder
            category={product.category}
            isOutOfStock={isOutOfStock}
          />
        )}

        {/* Etiqueta superior izquierda: Destacado o Agotado */}
        <div className="absolute top-3 left-3 z-10 flex flex-wrap gap-1.5">
          {isOutOfStock ? (
            <span className="rounded-full bg-ink/85 px-2.5 py-1 text-[11px] font-bold tracking-wide text-white uppercase shadow-sm backdrop-blur-xs">
              Agotado
            </span>
          ) : (
            featured && (
              <span className="rounded-full bg-accent px-2.5 py-1 text-[11px] font-bold tracking-wide text-white uppercase shadow-sm">
                Destacado
              </span>
            )
          )}
        </div>

        {/* Botón accesible de favoritos (usable con ratón y teclado) */}
        <button
          type="button"
          onClick={onToggleFavorite}
          aria-pressed={filled}
          aria-label={
            filled
              ? `Quitar ${product.displayName} de favoritos`
              : `Agregar ${product.displayName} a favoritos`
          }
          className="absolute top-3 right-3 z-10 grid size-10 place-items-center rounded-full bg-white/95 shadow-sm ring-1 ring-black/5 backdrop-blur transition hover:scale-110 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-dark active:scale-95"
        >
          <svg
            aria-hidden="true"
            viewBox="0 0 24 24"
            fill={filled ? "currentColor" : "none"}
            stroke="currentColor"
            strokeWidth={2.25}
            strokeLinecap="round"
            strokeLinejoin="round"
            className={`size-[18px] ${
              filled ? "text-accent" : "text-ink-soft"
            }`}
          >
            <path d="M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z" />
          </svg>
        </button>
      </div>

      <div className="flex flex-1 flex-col p-4">
        {/* Categoría pública */}
        <span className="w-fit rounded-md bg-canvas px-2 py-0.5 text-[11px] font-semibold tracking-wide text-ink-soft">
          {product.category}
        </span>

        {/* Nombre para mostrar al cliente */}
        <h3
          className={`mt-2 line-clamp-2 leading-snug font-bold ${
            isOutOfStock ? "text-ink-soft" : "text-ink"
          }`}
        >
          {product.displayName}
        </h3>

        {/* Precio público con ISV incluido */}
        <p
          className={`mt-auto pt-4 text-2xl leading-none font-extrabold tracking-tight tabular-nums ${
            isOutOfStock ? "text-muted" : "text-ink"
          }`}
        >
          {formatPrice(product.publicPrice)}
        </p>

        {/* Estado Disponible / Agotado claramente diferenciado */}
        <div className="mt-3 flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
          <span
            className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-semibold ring-1 ${status.badge}`}
          >
            <span aria-hidden="true" className={`size-2 rounded-full ${status.dot}`} />
            {status.label}
          </span>

          {variant === "favorites" && (
            <button
              type="button"
              onClick={onToggleFavorite}
              className="rounded text-xs font-semibold text-muted underline-offset-2 hover:text-accent-dark hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-dark"
            >
              Quitar de favoritos
            </button>
          )}
        </div>
      </div>
    </article>
  );
}

