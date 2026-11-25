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

## Sincronización con el agente (US-06)

El agente de [`sync-agent/`](../sync-agent/README.md) envía el inventario de Aspel a `/api/sync`. Todas esas rutas exigen el encabezado `X-Sync-Agent-Key`. El backend **no guarda la llave**: solo su hash SHA-256 en `Sync:AgentKeyHashes`. El protocolo está en [`docs/architecture.md`](../docs/architecture.md#5-flujo-de-sincronización-con-aspel-sae).

Para configurar la llave en desarrollo, desde la raíz del repositorio:

```powershell
.\sync-agent\scripts\New-SyncAgentKey.ps1 -Development
```

El script guarda la llave en los User Secrets del agente y el hash en los del backend. Hay que reiniciar el backend para que lea el hash. **Reemplaza** el `Sync:AgentKeyHashes` que hubiera: otras instalaciones locales con una llave distinta recibirán `401` (ver [`sync-agent/README.md`](../sync-agent/README.md)).

`seed-dev` y la sincronización se pisan: los productos `DEV-*` no vienen de Aspel, así que la primera sincronización del agente los **desactiva** (y, si hay más de 20, el freno de seguridad detiene la corrida). Para volver a verlos hay que ejecutar `seed-dev` otra vez; para quitarlos, borrarlos de la base. Sin `Sync:AgentKeyHashes`, la API arranca igual y responde `401` en `/api/sync` (lo avisa en el log).

| Opción (`Sync:*`) | Valor por defecto | Qué hace |
| :--- | :--- | :--- |
| `AgentKeyHashes` | *(vacío)* | Uno o dos hashes separados por coma (dos para rotar la llave). |
| `MaxBatchSize` | `2000` | Máximo de artículos por lote. |
| `AbandonAfterMinutes` | `15` | Una corrida en `running` con más tiempo se marca `failed` al iniciar la siguiente. |
| `FullRunMaxAgeHours` | `24` | Una corrida "sin cambios" solo se acepta si la última completa es más reciente. |
| `MaxDeactivationRatio` / `MaxDeactivationCount` | `0.10` / `20` | Freno de seguridad: no desactivar más del 10 % de los activos si son más de 20. |
| `UnchangedRunRetentionDays` | `30` | Días que se conservan las corridas "sin cambios" en `sync_runs`. |

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
