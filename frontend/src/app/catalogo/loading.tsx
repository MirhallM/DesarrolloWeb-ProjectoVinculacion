import { CatalogLoadingSkeleton } from "@/components/ui/CatalogStates";

export default function CatalogoLoading() {
  return (
    <main className="mx-auto max-w-7xl px-4 pt-6 pb-20 sm:px-6 lg:px-8">
      <div className="h-[340px] w-full animate-pulse rounded-3xl bg-canvas sm:h-[380px] lg:h-[420px]" />
      <div className="mt-8 lg:mt-10 lg:grid lg:grid-cols-[240px_1fr] lg:gap-8">
        <div className="hidden space-y-3 lg:block">
          <div className="h-4 w-28 animate-pulse rounded bg-canvas" />
          {Array.from({ length: 7 }).map((_, i) => (
            <div
              key={i}
              className="h-10 w-full animate-pulse rounded-xl bg-canvas"
            />
          ))}
        </div>
        <div>
          <div className="h-8 w-56 animate-pulse rounded-lg bg-canvas" />
          <CatalogLoadingSkeleton count={8} />
        </div>
      </div>
    </main>
  );
}

