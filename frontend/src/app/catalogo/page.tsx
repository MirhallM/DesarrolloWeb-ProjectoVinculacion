import type { Metadata } from "next";
import { CatalogSection } from "@/components/catalog/CatalogSection";
import { getInitialCatalogPage } from "@/lib/catalog";

export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Catálogo Público",
  description:
    "Explora todos los productos, categorías, precios y disponibilidad de Mini Super Nette's.",
};

export default async function CatalogoPage() {
  const { data, categories, error } = await getInitialCatalogPage({ page: 1 });

  return (
    <CatalogSection
      initialData={data}
      initialCategories={categories}
      initialError={error}
    />
  );
}
