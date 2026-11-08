"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import {
  Heart,
  Home,
  LayoutDashboard,
  LogIn,
  LogOut,
  Menu,
  Search,
  User,
  UserPlus,
  X,
} from "@/components/ui/Icons";
import { Logo } from "@/components/ui/Logo";
import { useStore } from "@/lib/store";

const normalize = (p: string | null) => (p ?? "/").replace(/(.)\/$/, "$1");

export function Header() {
  const {
    user,
    favorites,
    requireLogin,
    resetLoginIntent,
    logout,
    query,
    setQuery,
  } = useStore();
  const pathname = normalize(usePathname());
  const router = useRouter();
  const [menuOpen, setMenuOpen] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const onPointer = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setMenuOpen(false);
      }
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        setMenuOpen(false);
        setMobileOpen(false);
      }
    };
    document.addEventListener("mousedown", onPointer);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onPointer);
      document.removeEventListener("keydown", onKey);
    };
  }, []);

  const isCatalogRoute = pathname === "/" || pathname === "/catalogo";

  const onSearch = (value: string) => {
    setQuery(value);
    if (!isCatalogRoute) router.push("/catalogo");
  };

  const askLoginForFavorites = () => {
    setMobileOpen(false);
    requireLogin(
      "/favorites",
      "Inicia sesión para ver tus productos guardados."
    );
  };

  const iconBtn =
    "relative grid size-11 place-items-center rounded-full text-white transition hover:bg-white/15 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white";

  const favoritesBadge = user && favorites.length > 0 && (
    <span className="absolute top-1 right-0.5 grid min-w-5 place-items-center rounded-full bg-accent px-1 text-[11px] leading-5 font-bold text-white ring-2 ring-brand">
      {favorites.length}
    </span>
  );
  const heartIcon = (
    <Heart
      className="size-6"
      fill={pathname === "/favorites" ? "currentColor" : "none"}
    />
  );

  return (
    <header className="sticky top-0 z-30 bg-brand shadow-[0_1px_0_rgba(0,0,0,0.06)]">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <div className="flex items-center gap-2 py-3 sm:gap-3 lg:gap-6">
          {/* Botón de menú hamburguesa: móvil y tablet */}
          <button
            type="button"
            onClick={() => setMobileOpen((o) => !o)}
            aria-expanded={mobileOpen}
            aria-controls="mobile-menu"
            aria-label={mobileOpen ? "Cerrar menú" : "Abrir menú"}
            className={`${iconBtn} -ml-2 lg:hidden`}
          >
            {mobileOpen ? (
              <X className="size-6" />
            ) : (
              <Menu className="size-6" />
            )}
          </button>

          <Link
            href="/"
            aria-label="Inicio de Mini Super Nette's"
            className="shrink-0 rounded-lg focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
          >
            <Logo className="h-10 lg:h-12" eager />
          </Link>

          <SearchField
            value={query}
            onChange={onSearch}
            className="mx-auto hidden w-full max-w-xl lg:block"
          />

          <nav
            aria-label="Cuenta y favoritos"
            className="ml-auto flex shrink-0 items-center gap-1 lg:ml-0"
          >
            {user ? (
              <Link
                href="/favorites"
                aria-label="Favoritos"
                className={iconBtn}
              >
                {heartIcon}
                {favoritesBadge}
              </Link>
            ) : (
              <button
                type="button"
                onClick={askLoginForFavorites}
                aria-label="Favoritos"
                className={iconBtn}
              >
                {heartIcon}
              </button>
            )}

            {/* Controles de cuenta en escritorio */}
            <div className="hidden lg:block">
              {user ? (
                <div className="relative" ref={menuRef}>
                  <button
                    type="button"
                    onClick={() => setMenuOpen((o) => !o)}
                    className="ml-1 flex h-11 items-center gap-2 rounded-full bg-white/15 pr-4 pl-1 text-white transition hover:bg-white/25 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
                    aria-haspopup="menu"
                    aria-expanded={menuOpen}
                  >
                    <Avatar name={user.name} />
                    <span className="max-w-32 truncate text-sm font-semibold">
                      {user.name}
                    </span>
                  </button>
                  {menuOpen && (
                    <div
                      role="menu"
                      className="absolute right-0 mt-2 w-64 overflow-hidden rounded-2xl bg-white p-1.5 shadow-xl ring-1 ring-black/5"
                    >
                      <div className="px-3 py-2.5">
                        <p className="text-sm font-semibold text-ink">
                          {user.name}
                        </p>
                        <p className="truncate text-xs text-muted">
                          {user.email}
                        </p>
                      </div>
                      <div className="my-1 h-px bg-line" />
                      <MenuLink
                        href="/favorites"
                        onClick={() => setMenuOpen(false)}
                        icon={<Heart className="size-4" />}
                      >
                        Mis Favoritos
                      </MenuLink>
                      {user.isAdmin && (
                        <MenuLink
                          href="/admin"
                          onClick={() => setMenuOpen(false)}
                          icon={<LayoutDashboard className="size-4" />}
                        >
                          Panel de administración
                        </MenuLink>
                      )}
                      <MenuButton
                        icon={<LogOut className="size-4" />}
                        onClick={() => {
                          setMenuOpen(false);
                          logout();
                        }}
                      >
                        Cerrar sesión
                      </MenuButton>
                    </div>
                  )}
                </div>
              ) : (
                <Link
                  href="/login"
                  onClick={resetLoginIntent}
                  aria-label="Iniciar sesión"
                  className={iconBtn}
                >
                  <User className="size-6" />
                </Link>
              )}
            </div>
          </nav>
        </div>

        {/* Buscador en su propia fila para móvil y tablet */}
        <div className="pb-3 lg:hidden">
          <SearchField value={query} onChange={onSearch} />
        </div>
      </div>

      {/* Menú desplegable para móvil y tablet */}
      <div
        id="mobile-menu"
        className={`grid overflow-hidden transition-[grid-template-rows] duration-300 lg:hidden ${
          mobileOpen ? "grid-rows-[1fr]" : "grid-rows-[0fr]"
        }`}
        inert={!mobileOpen}
      >
        <div className="min-h-0">
          <nav
            aria-label="Menú móvil"
            className="border-t border-black/5 bg-white px-4 pt-3 pb-5 shadow-lg sm:px-6"
          >
            {user && (
              <div className="mb-2 flex items-center gap-3 rounded-2xl bg-brand-50 p-3">
                <Avatar name={user.name} tone="brand" />
                <div className="min-w-0">
                  <p className="truncate text-sm font-bold text-ink">
                    {user.name}
                  </p>
                  <p className="truncate text-xs text-muted">{user.email}</p>
                </div>
              </div>
            )}
            <MenuLink
              href="/catalogo"
              onClick={() => setMobileOpen(false)}
              icon={<Home className="size-5" />}
              large
              active={isCatalogRoute}
            >
              Catálogo de Productos
            </MenuLink>
            {user ? (
              <MenuLink
                href="/favorites"
                onClick={() => setMobileOpen(false)}
                icon={<Heart className="size-5" />}
                large
                active={pathname === "/favorites"}
              >
                Mis Favoritos
                {favorites.length > 0 && (
                  <span className="ml-auto rounded-full bg-accent px-2 text-xs leading-5 font-bold text-white">
                    {favorites.length}
                  </span>
                )}
              </MenuLink>
            ) : (
              <MenuButton
                icon={<Heart className="size-5" />}
                large
                onClick={askLoginForFavorites}
              >
                Mis Favoritos
              </MenuButton>
            )}
            {user?.isAdmin && (
              <MenuLink
                href="/admin"
                onClick={() => setMobileOpen(false)}
                icon={<LayoutDashboard className="size-5" />}
                large
              >
                Panel de administración
              </MenuLink>
            )}
            <div className="my-3 h-px bg-line" />
            {user ? (
              <MenuButton
                icon={<LogOut className="size-5" />}
                large
                onClick={() => {
                  setMobileOpen(false);
                  logout();
                }}
              >
                Cerrar sesión
              </MenuButton>
            ) : (
              <div className="grid gap-2 sm:grid-cols-2">
                <Link
                  href="/login"
                  onClick={() => {
                    setMobileOpen(false);
                    resetLoginIntent();
                  }}
                  className="flex h-12 items-center justify-center gap-2 rounded-xl bg-accent text-sm font-bold text-white hover:bg-accent-dark focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
                >
                  <LogIn className="size-4" /> Iniciar sesión
                </Link>
                <Link
                  href="/login?modo=registro"
                  onClick={() => {
                    setMobileOpen(false);
                    resetLoginIntent();
                  }}
                  className="flex h-12 items-center justify-center gap-2 rounded-xl border border-line text-sm font-bold text-ink hover:border-brand hover:text-brand-deep focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand"
                >
                  <UserPlus className="size-4" /> Crear cuenta
                </Link>
              </div>
            )}
          </nav>
        </div>
      </div>
    </header>
  );
}

function SearchField({
  value,
  onChange,
  className = "",
}: {
  value: string;
  onChange: (v: string) => void;
  className?: string;
}) {
  return (
    <form
      role="search"
      className={`relative ${className}`}
      onSubmit={(e) => e.preventDefault()}
    >
      <Search className="pointer-events-none absolute top-1/2 left-4 size-5 -translate-y-1/2 text-muted" />
      <input
        type="search"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder="Buscar productos por nombre..."
        aria-label="Buscar productos por nombre"
        className="h-12 w-full rounded-full border-0 bg-white pr-11 pl-12 text-[15px] text-ink shadow-sm ring-1 ring-black/5 outline-none placeholder:text-muted focus:ring-4 focus:ring-accent/40 [&::-webkit-search-cancel-button]:hidden"
      />
      {value && (
        <button
          type="button"
          onClick={() => onChange("")}
          className="absolute top-1/2 right-3 grid size-7 -translate-y-1/2 place-items-center rounded-full text-muted hover:bg-canvas hover:text-ink focus-visible:outline-2 focus-visible:outline-brand"
          aria-label="Limpiar búsqueda"
        >
          <X className="size-4" />
        </button>
      )}
    </form>
  );
}

function Avatar({
  name,
  tone = "white",
}: {
  name: string;
  tone?: "white" | "brand";
}) {
  return (
    <span
      className={`grid size-9 shrink-0 place-items-center rounded-full text-sm font-bold ${
        tone === "white" ? "bg-white text-brand-dark" : "bg-brand text-white"
      }`}
    >
      {name.charAt(0).toUpperCase()}
    </span>
  );
}

const itemClass = (large?: boolean, active?: boolean) =>
  `flex w-full items-center gap-3 rounded-xl px-3 text-left font-medium transition focus-visible:outline-2 focus-visible:outline-brand ${
    large ? "py-3 text-[15px]" : "py-2.5 text-sm"
  } ${
    active
      ? "bg-canvas text-ink"
      : "text-ink-soft hover:bg-canvas hover:text-ink"
  }`;

function MenuLink({
  href,
  icon,
  children,
  large,
  active,
  onClick,
}: {
  href: string;
  icon: ReactNode;
  children: ReactNode;
  large?: boolean;
  active?: boolean;
  onClick?: () => void;
}) {
  return (
    <Link
      href={href}
      onClick={onClick}
      role={large ? undefined : "menuitem"}
      aria-current={active ? "page" : undefined}
      className={itemClass(large, active)}
    >
      <span className="text-muted">{icon}</span>
      {children}
    </Link>
  );
}

function MenuButton({
  icon,
  children,
  onClick,
  large,
}: {
  icon: ReactNode;
  children: ReactNode;
  onClick: () => void;
  large?: boolean;
}) {
  return (
    <button
      type="button"
      role={large ? undefined : "menuitem"}
      onClick={onClick}
      className={itemClass(large)}
    >
      <span className="text-muted">{icon}</span>
      {children}
    </button>
  );
}

