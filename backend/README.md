# Backend - Catálogo Web Mini Super Nettes

API REST desarrollada con **ASP.NET Core (.NET 10 / `net10.0`)**, **Entity Framework Core**, **ASP.NET Core Identity** y **PostgreSQL 18** (`Npgsql`).

---

## Requisitos Previos

- **.NET 10 SDK** (`10.0.100` o superior; ver `backend/global.json`).
- **Docker Desktop** para levantar PostgreSQL 18 en desarrollo.
- **dotnet-ef** instalado globalmente:
  ```powershell
  dotnet tool install --global dotnet-ef --version "10.*"
  ```

---

## Configuración Inicial y Base de Datos

1. Levanta PostgreSQL desde la raíz del repositorio:
   ```powershell
   docker compose up -d
   ```

2. Configura la cadena de conexión con **User Secrets** (nunca en `appsettings.json`):
   ```powershell
   cd backend/src/backend
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=nettes_db;Username=admin;Password=TU_PASSWORD"
   ```

3. Aplica las migraciones a PostgreSQL:
   ```powershell
   dotnet ef database update
   ```

---

## Semillas (Seeders) Disponibles

| Comando (desde `backend/src/backend`) | Uso | Qué carga |
| :--- | :--- | :--- |
| `dotnet run -- seed` | **Producción y Desarrollo** | Siembra únicamente las tablas `categories` y `category_rules` desde `Data/Seed/categories.json`. Idempotente; seguro para pre-deploy. |
| `dotnet run -- seed-dev` | **Solo `Development`** | Ejecuta primero `seed` y carga **34 productos de prueba** (prefijo `DEV-`) categorizados con las reglas reales (30 visibles entre disponibles, agotados, con imagen y sin imagen para probar 2 páginas con `pageSize=24` + 4 ocultos para probar reglas de visibilidad). Es idempotente y se bloquea fuera de `Development`. |

---

## Cómo Probar la API

Inicia el servidor de desarrollo desde `backend/src/backend`:

```powershell
dotnet run
```

Al iniciar en modo `Development`, puedes probar los endpoints de tres maneras:

1. **Swagger UI (Navegador)**:
   - Interfaz interactiva: **`http://localhost:5039/swagger`** (o `https://localhost:7294/swagger` si usas el perfil `https`).
   - Documento OpenAPI JSON: **`http://localhost:5039/openapi/v1.json`**.
2. **Archivo HTTP (`REST Client`)**:
   - Abre [`backend/src/backend/backend.http`](src/backend/backend.http) en Antigravity / VS Code (con la extensión *REST Client*) para ejecutar las consultas de ejemplo directamente.
3. **Endpoints Públicos Disponibles (US-08)**:
   - `GET /api/products?page=1&pageSize=24&sort=name&order=asc` (`sort`: `name` \| `price`; `order`: `asc` \| `desc`; `pageSize` máx. `100`).
   - `GET /api/products/{id}` (`404 ProblemDetails` si no existe o está oculto).
   - `GET /api/categories` (ordenadas por `position` con `"Otros"` al final y conteo de productos visibles).

---

## Pruebas Automatizadas

Desde `backend/`:

```powershell
# Compilar solución
dotnet build Backend.slnx -c Release

# Ejecutar pruebas unitarias (sin PostgreSQL)
dotnet test --solution Backend.slnx -c Release -- --filter-not-trait "Category=Integration"
```

Para incluir las **pruebas de integración contra PostgreSQL real** (crean y borran bases temporales automáticamente):

```powershell
$env:BACKEND_TEST_CONNECTION = "Host=localhost;Port=5432;Database=nettes_db;Username=admin;Password=TU_PASSWORD"
dotnet test --solution Backend.slnx -c Release
```
