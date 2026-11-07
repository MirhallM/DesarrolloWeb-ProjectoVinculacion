"use client";

import { useEffect, useState } from "react";
import Image from "next/image";
import Link from "next/link";
import {
  ArrowRight,
  ChevronLeft,
  ChevronRight,
  ImagePlus,
} from "@/components/ui/Icons";
import { useStore } from "@/lib/store";

export function HeroBanner() {
  const { banners, user } = useStore();
  const active = banners.filter((b) => b.active);
  const [index, setIndex] = useState(0);
  const count = active.length;
  const current = count ? active[index % count] : undefined;

  useEffect(() => {
    if (count < 2) return;
    const t = setInterval(() => setIndex((i) => (i + 1) % count), 6500);
    return () => clearInterval(t);
  }, [count]);

  if (!current) {
    return (
      <div className="flex h-[260px] w-full flex-col items-center justify-center rounded-3xl border-2 border-dashed border-brand/40 bg-brand-50 px-6 text-center sm:h-[340px]">
        <ImagePlus className="size-8 text-brand-dark" />
        <p className="mt-3 font-bold text-brand-deep">
          Espacio para banner promocional
        </p>
        <p className="mt-1 text-sm text-ink-soft">
          Activa un banner en el panel de administración para mostrarlo aquí.
        </p>
        {user?.isAdmin && (
          <Link
            href="/admin"
            className="mt-4 rounded-full bg-accent px-5 py-2 text-sm font-semibold text-white hover:bg-accent-dark focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
          >
            Gestionar banners
          </Link>
        )}
      </div>
    );
  }

  const go = (d: number) => setIndex((i) => (i + d + count) % count);
  const shown = index % count;

  return (
    <section
      aria-roledescription="carrusel"
      aria-label="Promociones destacadas"
      className="relative isolate overflow-hidden rounded-3xl bg-brand-deep"
    >
      <div className="relative h-[340px] sm:h-[380px] lg:h-[420px]">
        {active.map((b, i) => (
          <Image
            key={b.id}
            src={b.image}
            alt=""
            fill
            sizes="(min-width: 1280px) 1216px, 100vw"
            loading={i === 0 ? "eager" : "lazy"}
            className={`object-cover transition-opacity duration-700 ${
              i === shown ? "opacity-100" : "opacity-0"
            }`}
          />
        ))}
        <div className="absolute inset-0 bg-gradient-to-t from-brand-deep via-brand-deep/70 to-brand-deep/10 sm:bg-gradient-to-r sm:from-brand-deep sm:via-brand-deep/75 sm:to-transparent" />

        <div
          key={current.id}
          className="relative flex h-full max-w-xl flex-col justify-end px-6 pb-16 sm:justify-center sm:px-12 sm:pb-0"
        >
          <span className="w-fit rounded-full bg-white/15 px-3 py-1 text-[11px] font-bold tracking-[0.14em] text-white uppercase ring-1 ring-white/25 backdrop-blur sm:text-xs">
            Esta semana en Nette&apos;s
          </span>
          <h1 className="mt-3 text-[1.75rem] leading-[1.1] font-extrabold tracking-tight text-balance text-white sm:mt-4 sm:text-5xl sm:leading-[1.08]">
            {current.title}
          </h1>
          {current.subtitle && (
            <p className="mt-3 max-w-md text-sm leading-relaxed text-white/85 sm:text-lg">
              {current.subtitle}
            </p>
          )}
          <a
            href="#catalogo-productos"
            className="group mt-5 inline-flex w-fit items-center gap-2 rounded-full bg-accent px-5 py-3 text-sm font-bold text-white shadow-lg shadow-black/20 transition hover:bg-accent-dark focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white sm:mt-7 sm:px-6 sm:py-3.5"
          >
            {current.cta}
            <ArrowRight className="size-4 transition group-hover:translate-x-0.5" />
          </a>
        </div>
      </div>

      {count > 1 && (
        <div className="absolute bottom-5 left-6 flex items-center gap-3 sm:right-8 sm:bottom-7 sm:left-auto">
          <div className="flex gap-1.5">
            {active.map((b, i) => (
              <button
                key={b.id}
                type="button"
                onClick={() => setIndex(i)}
                aria-label={`Mostrar banner ${i + 1}`}
                aria-current={i === shown}
                className={`h-2 rounded-full transition-all focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white ${
                  i === shown
                    ? "w-7 bg-white"
                    : "w-2 bg-white/50 hover:bg-white/80"
                }`}
              />
            ))}
          </div>
          <div className="hidden gap-1.5 sm:flex">
            <button
              type="button"
              onClick={() => go(-1)}
              aria-label="Banner anterior"
              className="grid size-9 place-items-center rounded-full bg-white/90 text-ink hover:bg-white focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
            >
              <ChevronLeft className="size-4" />
            </button>
            <button
              type="button"
              onClick={() => go(1)}
              aria-label="Banner siguiente"
              className="grid size-9 place-items-center rounded-full bg-white/90 text-ink hover:bg-white focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
            >
              <ChevronRight className="size-4" />
            </button>
          </div>
        </div>
      )}
    </section>
  );
}

