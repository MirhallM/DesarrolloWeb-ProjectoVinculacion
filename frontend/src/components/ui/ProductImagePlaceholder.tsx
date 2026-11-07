interface ProductImagePlaceholderProps {
  category?: string;
  isOutOfStock?: boolean;
}

/**
 * Imagen genérica diseñada para productos que no cuentan con fotografía en el servidor
 * (cumple con docs/business-rules.md Sección 6 y US-10).
 */
export function ProductImagePlaceholder({
  category,
  isOutOfStock = false,
}: ProductImagePlaceholderProps) {
  return (
    <div
      role="img"
      aria-label={
        category
          ? `Imagen genérica de producto en categoría ${category}`
          : "Imagen genérica de producto sin fotografía"
      }
      className={`relative flex size-full flex-col items-center justify-center overflow-hidden bg-gradient-to-br from-brand-50 via-canvas to-brand-100/60 p-4 text-center select-none ${
        isOutOfStock ? "opacity-60 grayscale-[50%]" : ""
      }`}
    >
      {/* Patrón decorativo de fondo */}
      <svg
        aria-hidden="true"
        viewBox="0 0 200 150"
        className="pointer-events-none absolute inset-0 size-full opacity-35"
         preserveAspectRatio="xMidYMid slice"
      >
        <circle cx="24" cy="24" r="36" fill="#e3f0cf" />
        <circle cx="180" cy="130" r="44" fill="#e3f0cf" />
      </svg>

      {/* Ícono ilustrativo de canasta / producto Mini Super Nette's */}
      <div className="relative z-10 grid size-16 place-items-center rounded-2xl bg-white/90 text-brand-dark shadow-xs ring-1 ring-brand/20">
        <svg
          aria-hidden="true"
          viewBox="0 0 48 48"
          fill="none"
          className="size-9"
        >
          {/* Asa de la canasta */}
          <path
            d="M16 20L21 10M32 20L27 10"
            stroke="currentColor"
            strokeWidth="2.75"
            strokeLinecap="round"
          />
          {/* Borde superior de la canasta */}
          <rect
            x="8"
            y="20"
            width="32"
            height="5"
            rx="2.5"
            fill="#7cb334"
            fillOpacity="0.2"
            stroke="currentColor"
            strokeWidth="2.5"
          />
          {/* Cuerpo de la canasta */}
          <path
            d="M11 25L13.5 37.2C13.88 39.06 15.52 40.4 17.42 40.4H30.58C32.48 40.4 34.12 39.06 34.5 37.2L37 25"
            fill="#ffffff"
            stroke="currentColor"
            strokeWidth="2.5"
            strokeLinejoin="round"
          />
          {/* Ranuras verticales */}
          <path
            d="M20 29V36M24 29V36M28 29V36"
            stroke="#fb7806"
            strokeWidth="2.25"
            strokeLinecap="round"
          />
        </svg>
      </div>

      <span className="relative z-10 mt-2.5 text-[11px] font-bold tracking-wider text-brand-deep/80 uppercase">
        Mini Super Nette&apos;s
      </span>
      <span className="relative z-10 mt-0.5 text-[11px] font-medium text-muted">
        Sin imagen disponible
      </span>
    </div>
  );
}

