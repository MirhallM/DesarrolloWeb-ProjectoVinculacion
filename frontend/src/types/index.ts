/**
 * Definiciones e interfaces TypeScript para el Catálogo Público de Mini Super Nette's.
 * Cumple estrictamente con docs/business-rules.md (Sección 7 y US-08):
 * Las respuestas públicas NO incluyen el código de artículo (CVE_ART),
 * ni la descripción original de Aspel, ni la cantidad exacta en existencia.
 */

export type ProductAvailability = "available" | "out_of_stock";

export interface PublicProduct {
  /** Identificador único (UUID) del producto en PostgreSQL */
  id: string;
  /** Nombre para mostrar al cliente (abreviaturas expandidas y en formato título) */
  displayName: string;
  /** Precio público en Lempiras con ISV incluido (CVE_PRECIO = 1) */
  publicPrice: number;
  /** Categoría asignada por las reglas de la aplicación (o "Otros") */
  category: string;
  /** Estado público de disponibilidad: "available" (Disponible) o "out_of_stock" (Agotado) */
  availability: ProductAvailability;
  /** URL o ruta de la imagen del producto; null o undefined si usa la imagen genérica */
  imageUrl?: string | null;
  /** Texto alternativo opcional para accesibilidad */
  imageAlt?: string | null;
  /** Indica si el producto está marcado como destacado en el catálogo */
  isFeatured?: boolean;
}

export interface Banner {
  id: string;
  title: string;
  subtitle: string;
  cta: string;
  image: string;
  active: boolean;
  createdAt: string;
}

export interface PaginatedCatalogResponse {
  items: PublicProduct[];
  page: number;
  pageSize: number;
  totalCount: number;
  lastSyncedAt?: string | null;
}

export interface UserSession {
  name: string;
  email: string;
  isAdmin: boolean;
}
