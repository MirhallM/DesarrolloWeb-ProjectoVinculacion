# Frontend - Catálogo Web Mini Super Nettes

Aplicación web desarrollada con **Next.js 16+ (App Router)**, **TypeScript** y **Tailwind CSS v4** sobre **Node.js 22 LTS**.

---

## Requisitos Previos

- **Node.js**: `>= 22.0.0` (LTS)
- **Gestor de paquetes**: `npm` (incluido con Node.js)
- **Backend activo**: API de ASP.NET Core (`.NET 10`) corriendo para el consumo de endpoints (por defecto en `http://localhost:5039`).

---

## Configuración de Variables de Entorno

Copia `frontend/.env.example` a `frontend/.env` (o `.env.local`):

```env
# URL interna del backend ASP.NET Core (.NET 10) usada por Server Components y el proxy /api/*
API_URL=http://localhost:5039

# Clave secreta para firmar/validar cookies de sesión BFF (mínimo 32 caracteres)
AUTH_SECRET=ReemplazarConUnaCadenaAleatoriaSegura_Minimo32Caracteres
```

> El navegador nunca llama directamente al backend ni a las bases de datos: todas las peticiones del cliente a `/api/products` y `/api/categories` van al mismo origen y Next.js las reenvía a `API_URL` mediante `rewrites()` en `next.config.ts`.

---

## Comandos Disponibles

Todos los comandos deben ejecutarse posicionado dentro del directorio `/frontend`:

```bash
cd frontend
```

| Comando | Descripción |
| :--- | :--- |
| `npm install` | Instala las dependencias del proyecto definidas en `package.json`. |
| `npm run dev` | Inicia el servidor de desarrollo en `http://localhost:3000` con recarga en caliente. |
| `npm run build` | Compila y optimiza la aplicación para producción en la carpeta `.next/`. |
| `npm run start` | Inicia el servidor de producción (requiere haber ejecutado `npm run build` previamente). |
| `npm run lint` | Ejecuta **ESLint** para analizar el código y detectar errores de estilo o tipado. |
| `npx tsc --noEmit` | Verifica el tipado estricto de TypeScript sin generar archivos compilados. |

---

## Estructura del Proyecto (`src/`)

```text
src/
├── app/                      # Enrutamiento App Router y layouts
│   ├── (auth)/login/         # Vista de inicio de sesión (/login)
│   ├── api/auth/             # Route Handler BFF para sesión con cookies HTTP-only
│   ├── catalogo/             # Catálogo público (/catalogo) con loading.tsx y error.tsx
│   ├── globals.css           # Estilos globales y tokens de Tailwind CSS v4
│   ├── icon.png              # Favicon de la aplicación
│   ├── layout.tsx            # Root Layout de la aplicación
│   └── page.tsx              # Página de inicio (/) que renderiza el catálogo público
├── components/
│   ├── catalog/              # Componentes del dominio de catálogo público
│   │   ├── CatalogSection.tsx        # Paginación, ordenamiento, filtros y estados
│   │   ├── CatalogStates.tsx         # Skeleton de carga, error con reintento y estado vacío
│   │   ├── FilterSidebar.tsx         # Barra lateral de categorías reales (GET /api/categories)
│   │   ├── HeroBanner.tsx            # Carrusel de banners promocionales
│   │   ├── Pagination.tsx            # Controles accesibles de paginación
│   │   ├── ProductCard.tsx           # Tarjeta pública (nombre, precio, estado, categoría, imagen)
│   │   └── ProductImagePlaceholder.tsx # Imagen genérica cuando imageUrl es null o falla
│   ├── layout/               # Componentes estructurales (Header con buscador, favoritos y cuenta)
│   └── ui/                   # Componentes visuales compartidos (Logo, Icons)
├── lib/
│   ├── api.ts                # Cliente HTTP centralizado (apiFetch, ApiError con ProblemDetails)
│   ├── catalog.ts            # Servicios tipados (getProducts, getProductById, getCategories)
│   ├── store.tsx             # Contexto de interfaz (búsqueda, favoritos y sesión en memoria)
│   └── utils.ts              # Funciones utilitarias (formatPrice en Lempiras, formatDate)
└── types/
    ├── catalog.ts            # Contratos DTO públicos que mapean las respuestas del backend .NET
    └── index.ts              # Punto único de re-exportación de tipos
```

---

## Reglas de Arquitectura

1. **Sin conexión directa a BD**: El frontend jamás se conecta directamente a PostgreSQL ni a SQL Server; consume únicamente la API de `.NET 10` mediante `src/lib/api.ts` y el proxy `/api/*`.
2. **Solo campos públicos (`docs/business-rules.md`)**: En vistas y tipos públicos nunca se incluye el código de artículo (`CVE_ART`), la descripción original de Aspel ni la cantidad numérica en existencia (`EXIST`).
3. **React Server Components (RSC) por defecto**: Usar `"use client"` únicamente cuando el componente requiera estado, `useEffect` o eventos del navegador.
4. **Resiliencia ante errores**: Las fallas de conexión o respuestas de error (`ProblemDetails`) muestran un estado controlado dentro de la sección afectada sin romper toda la página.
5. **Autenticación segura**: Prohibido almacenar tokens JWT en `localStorage` o `sessionStorage`.
