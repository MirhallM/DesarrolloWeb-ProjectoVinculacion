# Frontend - Catálogo Web Mini Super Nettes

Aplicación web desarrollada con **Next.js 16+ (App Router)**, **TypeScript** y **Tailwind CSS v4** sobre **Node.js 22 LTS**.

---

## Requisitos Previos

- **Node.js**: `>= 22.0.0` (LTS)
- **Gestor de paquetes**: `npm` (incluido con Node.js)
- **Backend activo**: API de ASP.NET Core (`.NET 8`) corriendo para el consumo de endpoints.

---

## Configuración de Variables de Entorno

Crear un archivo `.env` (o `.env.local`) en la raíz de `/frontend` con las siguientes variables:


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
├── app/                  # Enrutamiento App Router y Layouts
│   ├── (auth)/login/     # Vista de inicio de sesión (/login)
│   ├── api/auth/         # Route Handlers (BFF) para manejo de JWT en cookies HTTP-only
│   ├── catalogo/         # Vista pública del catálogo (/catalogo)
│   ├── globals.css       # Estilos globales e importación de Tailwind CSS
│   ├── layout.tsx        # Root Layout de la aplicación
│   └── page.tsx          # Página de inicio (/)
├── components/
│   ├── layout/           # Componentes estructurales (Navbar, Sidebar, Footer)
│   └── ui/               # Componentes visuales reutilizables (Botones, Cards, Inputs)
├── lib/
│   ├── api.ts            # Cliente Fetch centralizado hacia el backend de .NET
│   └── utils.ts          # Funciones utilitarias (formateo de moneda, fechas)
└── types/
    └── index.ts          # Interfaces y DTOs que mapean las respuestas del backend
```

---

## Reglas de Arquitectura

1. **Sin conexión directa a BD**: El frontend jamás se conecta directamente a PostgreSQL ni a SQL Server; consume únicamente la API de `.NET 8` mediante `apiFetch` (`src/lib/api.ts`).
2. **React Server Components (RSC) por defecto**: Usar `"use client"` únicamente cuando el componente requiera estado (`useState`, `useEffect`) o eventos del navegador (`onClick`, `onChange`).
3. **Autenticación segura**: Prohibido almacenar tokens JWT en `localStorage` o `sessionStorage`. La sesión se gestiona mediante cookies `HTTP-only` a través de `src/app/api/auth/`.

