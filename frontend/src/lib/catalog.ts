import { ApiError, apiFetch } from "@/lib/api";
import type {
  CatalogQueryParams,
  PagedResult,
  PublicCategory,
  PublicProduct,
} from "@/types";

export const DEFAULT_PAGE_SIZE = 24;

/**
 * Obtiene una página de productos visibles desde GET /api/products.
 */
export async function getProducts(
  params: CatalogQueryParams = {},
  options: { signal?: AbortSignal } = {}
): Promise<PagedResult<PublicProduct>> {
  const searchParams = new URLSearchParams();
  searchParams.set("page", String(params.page ?? 1));
  searchParams.set("pageSize", String(params.pageSize ?? DEFAULT_PAGE_SIZE));
  searchParams.set("sort", params.sort ?? "name");
  searchParams.set("order", params.order ?? "asc");

  return apiFetch<PagedResult<PublicProduct>>(
    `/products?${searchParams.toString()}`,
    {
      cache: "no-store",
      signal: options.signal,
    }
  );
}

/**
 * Obtiene el detalle público de un producto visible desde GET /api/products/{id}.
 */
export async function getProductById(
  id: string,
  options: { signal?: AbortSignal } = {}
): Promise<PublicProduct> {
  return apiFetch<PublicProduct>(`/products/${encodeURIComponent(id)}`, {
    cache: "no-store",
    signal: options.signal,
  });
}

/**
 * Obtiene las categorías ordenadas desde GET /api/categories.
 */
export async function getCategories(
  options: { signal?: AbortSignal } = {}
): Promise<PublicCategory[]> {
  return apiFetch<PublicCategory[]>("/categories", {
    cache: "no-store",
    signal: options.signal,
  });
}

/**
 * Carga inicial tolerante a fallos para Server Components:
 * consulta productos y categorías reales en paralelo sin romper el layout si el backend no responde.
 */
export async function getInitialCatalogPage(
  params: CatalogQueryParams = {}
): Promise<{
  data: PagedResult<PublicProduct> | null;
  categories: PublicCategory[];
  error: string | null;
}> {
  try {
    const [data, categories] = await Promise.all([
      getProducts(params),
      getCategories().catch(() => []),
    ]);
    return { data, categories, error: null };
  } catch (err) {
    const message =
      err instanceof ApiError
        ? err.message
        : "No fue posible conectar con el catálogo en este momento.";
    return { data: null, categories: [], error: message };
  }
}
