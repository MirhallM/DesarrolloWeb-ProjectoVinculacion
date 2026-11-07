"use client";

import { CatalogErrorState } from "@/components/ui/CatalogStates";

export default function CatalogoError({
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  return (
    <main className="mx-auto max-w-7xl px-4 py-12 sm:px-6 lg:px-8">
      <CatalogErrorState onRetry={reset} />
    </main>
  );
}

