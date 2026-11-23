import { CatalogLoadingSkeleton } from "@/components/catalog/CatalogStates";

export default function CatalogoLoading() {
  return (
    <main className="mx-auto w-full max-w-7xl px-4 pt-6 pb-20 sm:px-6 lg:px-8">
      <div className="border-b border-line pb-4">
        <div className="h-8 w-56 animate-pulse rounded-lg bg-canvas" />
        <div className="mt-2 h-4 w-36 animate-pulse rounded bg-canvas" />
      </div>
      <CatalogLoadingSkeleton count={8} />
    </main>
  );
}
