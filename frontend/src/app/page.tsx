import { CatalogSection } from "@/components/catalog/CatalogSection";
import { getInitialCatalogPage } from "@/lib/catalog";

export const dynamic = "force-dynamic";

export default async function HomePage() {
  const { data, categories, error } = await getInitialCatalogPage({ page: 1 });

  return (
    <CatalogSection
      initialData={data}
      initialCategories={categories}
      initialError={error}
    />
  );
}
