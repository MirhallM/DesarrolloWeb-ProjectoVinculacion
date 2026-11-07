import type { Metadata } from "next";
import { CatalogView } from "@/components/layout/CatalogView";

export const metadata: Metadata = {
  title: "Catálogo Público",
  description:
    "Explora todos los productos, precios y disponibilidad de Mini Super Nette's.",
};

export default function CatalogoPage() {
  return <CatalogView />;
}
