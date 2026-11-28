"use client";

import { useEffect } from "react";
import { Heart } from "@/components/ui/Icons";

export interface ConfirmRemoveFavoriteModalProps {
  productName: string;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmRemoveFavoriteModal({
  productName,
  onConfirm,
  onCancel,
}: ConfirmRemoveFavoriteModalProps) {
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onCancel();
      }
    };

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onCancel]);

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-ink/35 p-4 backdrop-blur-[2px]"
      onClick={onCancel}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-remove-fav-title"
        onClick={(e) => e.stopPropagation()}
        className="w-full max-w-sm rounded-3xl bg-white p-6 shadow-2xl ring-1 ring-black/5"
      >
        <div className="flex items-start gap-3.5">
          <div className="grid size-10 shrink-0 place-items-center rounded-full bg-accent-50 text-accent">
            <Heart className="size-5" fill="currentColor" />
          </div>
          <div className="min-w-0">
            <h2
              id="confirm-remove-fav-title"
              className="text-base font-extrabold text-ink"
            >
              ¿Quitar de favoritos?
            </h2>
            <p className="mt-1 text-sm leading-relaxed text-ink-soft">
              Se quitará{" "}
              <span className="font-semibold text-ink">{productName}</span> de
              tu lista de guardados.
            </p>
          </div>
        </div>

        <div className="mt-5 flex justify-end gap-2.5">
          <button
            type="button"
            onClick={onCancel}
            className="h-10 rounded-xl border border-line px-4 text-sm font-semibold text-ink-soft transition hover:bg-canvas hover:text-ink focus-visible:outline-2 focus-visible:outline-brand"
          >
            Cancelar
          </button>
          <button
            type="button"
            autoFocus
            onClick={onConfirm}
            className="h-10 rounded-xl bg-accent px-4 text-sm font-bold text-white transition hover:bg-accent-dark focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
          >
            Sí, quitar
          </button>
        </div>
      </div>
    </div>
  );
}
