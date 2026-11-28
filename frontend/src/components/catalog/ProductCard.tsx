"use client";

import { useState } from "react";
import Image from "next/image";
import { formatPrice } from "@/lib/utils";
import type { PublicCategoryRef } from "@/types";
import { ProductImagePlaceholder } from "./ProductImagePlaceholder";

export interface ProductCardItem {
  id: string;
  name: string;
  price: number | null;
  available: boolean;
  category: PublicCategoryRef;
  imageUrl: string | null;
}

export interface ProductCardProps {
  product: ProductCardItem;
  favorite?: boolean;
  onToggleFavorite?: () => void;
  variant?: "catalog" | "favorites";
  /** Marca como "No disponible" un favorito cuyo producto quedó oculto posteriormente. */
  unavailable?: boolean;
  /** Sugerencia `sizes` para el layout responsive del grid. */
  sizes?: string;
  /** Carga prioritaria para la primera fila visible. */
  eager?: boolean;
}

/**
 * Tarjeta pública de producto para el catálogo y la vista de favoritos.
 * Muestra estrictamente los campos públicos autorizados en docs/business-rules.md (Sección 7):
 * nunca incluye código CVE_ART, descripción cruda de Aspel ni cantidad en existencia.
 */
export function ProductCard({
  product,
  favorite = false,
  onToggleFavorite,
  variant = "catalog",
  unavailable = false,
  sizes = "(min-width: 1280px) 22vw, (min-width: 1024px) 30vw, (min-width: 640px) 50vw, 100vw",
  eager = false,
}: ProductCardProps) {
  const [imageError, setImageError] = useState(false);
  const isMuted = unavailable || !product.available;
  const filled = favorite || variant === "favorites";
  const showRealImage = Boolean(product.imageUrl) && !imageError;

  const statusLabel = unavailable
    ? "No disponible"
    : product.available
      ? "Disponible"
      : "Agotado";

  return (
    <article
      aria-label={`${product.name} - ${statusLabel}`}
      className={`group relative flex flex-col overflow-hidden rounded-2xl ring-1 transition duration-200 focus-within:ring-2 focus-within:ring-brand ${
        isMuted
          ? "bg-canvas/45 ring-line/90"
          : "bg-white ring-line hover:-translate-y-0.5 hover:shadow-[0_12px_32px_-12px_rgba(31,36,33,0.25)] hover:ring-brand/40"
      }`}
    >
      <div className="relative aspect-[4/3] overflow-hidden bg-canvas">
        {showRealImage && product.imageUrl ? (
          <Image
            src={product.imageUrl}
            alt={product.name}
            fill
            unoptimized
            sizes={sizes}
            loading={eager ? "eager" : "lazy"}
            onError={() => setImageError(true)}
            className={`object-cover transition duration-500 group-hover:scale-105 ${
              isMuted ? "opacity-55 grayscale-[50%]" : ""
            }`}
          />
        ) : (
          <ProductImagePlaceholder
            category={product.category.name}
            isOutOfStock={isMuted}
          />
        )}

        {isMuted && (
          <div className="absolute top-3 left-3 z-10">
            <span className="rounded-full bg-ink/85 px-2.5 py-1 text-[11px] font-bold tracking-wide text-white uppercase shadow-sm backdrop-blur-xs">
              {unavailable ? "No disponible" : "Agotado"}
            </span>
          </div>
        )}

        {onToggleFavorite && (
          <button
            type="button"
            onClick={onToggleFavorite}
            aria-pressed={filled}
            aria-label={
              filled
                ? `Quitar ${product.name} de favoritos`
                : `Agregar ${product.name} a favoritos`
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
        )}
      </div>

      <div className="flex flex-1 flex-col p-4">
        <span className="w-fit rounded-md bg-canvas px-2 py-0.5 text-[11px] font-semibold tracking-wide text-ink-soft">
          {product.category.name}
        </span>

        <h3
          className={`mt-2 line-clamp-2 leading-snug font-bold ${
            isMuted ? "text-ink-soft" : "text-ink"
          }`}
        >
          {product.name}
        </h3>

        {unavailable || product.price === null ? (
          <p className="mt-auto pt-4 text-sm leading-none font-semibold text-muted">
            No disponible actualmente
          </p>
        ) : (
          <p
            className={`mt-auto pt-4 text-2xl leading-none font-extrabold tracking-tight tabular-nums ${
              isMuted ? "text-muted" : "text-ink"
            }`}
          >
            {formatPrice(product.price)}
          </p>
        )}

        <div className="mt-3 flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
          <span
            className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-semibold ring-1 ${
              unavailable
                ? "bg-canvas text-ink-soft ring-line"
                : !product.available
                  ? "bg-red-50 text-red-700 ring-red-200"
                  : "bg-brand-50 text-brand-deep ring-brand/25"
            }`}
          >
            <span
              aria-hidden="true"
              className={`size-2 rounded-full ${
                unavailable
                  ? "bg-muted"
                  : !product.available
                    ? "bg-red-500"
                    : "bg-brand"
              }`}
            />
            {statusLabel}
          </span>

          {variant === "favorites" && onToggleFavorite && (
            <button
              type="button"
              onClick={onToggleFavorite}
              className="text-xs font-semibold text-muted underline-offset-2 transition hover:text-accent-dark hover:underline focus-visible:outline-2 focus-visible:outline-brand"
            >
              Quitar de favoritos
            </button>
          )}
        </div>
      </div>
    </article>
  );
}
