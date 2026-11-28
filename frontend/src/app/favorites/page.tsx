import type { Metadata } from "next";
import { FavoritesView } from "@/components/favorites/FavoritesView";

export const metadata: Metadata = {
  title: "Mis Favoritos",
};

export default function FavoritesPage() {
  return <FavoritesView />;
}
