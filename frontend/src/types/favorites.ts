import type { PublicCategoryRef } from "./catalog";

export type FavoriteAvailabilityStatus =
  | "available"
  | "out_of_stock"
  | "unavailable";

export interface FavoriteItem {
  productId: string;
  name: string;
  price: number | null;
  isVisible: boolean;
  available: boolean;
  availabilityStatus: FavoriteAvailabilityStatus;
  category: PublicCategoryRef;
  imageUrl: string | null;
  savedAt: string;
}
