/**
 * Contratos TypeScript públicos del Catálogo Web (alineados con backend/Catalog/CatalogDtos.cs).
 * No incluyen campos internos (código CVE_ART, descripción original de Aspel ni existencia numérica).
 */

export interface PublicCategoryRef {
  slug: string;
  name: string;
}

export interface PublicProduct {
  id: string;
  /** Nombre para mostrar al cliente (abreviaturas expandidas y en formato título). */
  name: string;
  /** Precio público en Lempiras con ISV incluido (2 decimales). */
  price: number;
  /** true si tiene existencia (> 0), false si está agotado. */
  available: boolean;
  category: PublicCategoryRef;
  /** Ruta o URL de la imagen, o null si debe mostrarse la imagen genérica. */
  imageUrl: string | null;
}

export interface PublicCategory {
  slug: string;
  name: string;
  productCount: number;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export type ProductSortField = "name" | "price";
export type SortDirection = "asc" | "desc";

export interface CatalogQueryParams {
  page?: number;
  pageSize?: number;
  sort?: ProductSortField;
  order?: SortDirection;
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
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

export interface UserSession {
  id: string;
  name: string;
  email: string;
  roles: string[];
  isAdmin: boolean;
}
