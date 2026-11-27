import { apiFetch } from "@/lib/api";
import type { FavoriteItem } from "@/types";

export async function getFavorites(
  options?: RequestInit
): Promise<FavoriteItem[]> {
  return apiFetch<FavoriteItem[]>("/favorites", {
    cache: "no-store",
    ...options,
  });
}

export async function addFavorite(productId: string): Promise<FavoriteItem> {
  return apiFetch<FavoriteItem>(
    `/favorites/${encodeURIComponent(productId)}`,
    {
      method: "POST",
    }
  );
}

export async function removeFavorite(productId: string): Promise<void> {
  await apiFetch<void>(`/favorites/${encodeURIComponent(productId)}`, {
    method: "DELETE",
  });
}
