# Catálogo Web – Mini Super Nettes

![CI](https://github.com/MirhallM/DesarrolloWeb-ProjectoVinculacion/actions/workflows/ci.yml/badge.svg)

Proyecto de Vinculación – Desarrollo Web (Q4 2026).

Aplicación web para que los clientes de Mini Super Nettes consulten el catálogo de productos, con precios y existencias tomados del sistema administrativo Aspel SAE. Incluye un panel administrativo para gestionar imágenes, productos destacados y contenido promocional.

## Alcance

- **Catálogo público:** productos con descripción, precio, imagen y existencia.
- **Búsqueda y filtros:** por nombre, código o categoría.
- **Cuentas de usuario:** registro, inicio de sesión y roles (`User`, `Admin`).
- **Favoritos:** guardar productos para consultarlos después.
- **Panel administrativo:** imágenes, productos destacados y contenido promocional.
- **Sincronización de inventario:** actualización periódica de precios y existencias desde Aspel SAE.
- **Monitoreo y respaldos:** registro de sincronizaciones y copias de seguridad de la base de datos y las imágenes.

**Fuera de alcance:** modificar el inventario de Aspel SAE y procesar pagos o pedidos en línea. La disponibilidad que muestra el sitio es informativa y no reserva mercancía.

## Arquitectura

| Capa | Tecnología |
|---|---|
| Frontend | Next.js con TypeScript |
| Backend | ASP.NET Core Web API (.NET 10, C#) |
| Base de datos de la aplicación | PostgreSQL 17 |
| ORM | Entity Framework Core con Npgsql |
| Sistema de origen | Aspel SAE 7.0 (SQL Server, **solo lectura**) |
| Imágenes | Archivos en el servidor; rutas y metadatos en PostgreSQL |
| Despliegue | Máquina virtual con Windows 11, acceso público por HTTPS |

El frontend se comunica únicamente con la API del backend; el navegador nunca se conecta a las bases de datos. Aspel SAE es la fuente oficial de productos, precios y existencias. Un proceso automático copia esos datos a PostgreSQL, y las consultas públicas se hacen sobre esa copia.

## Estructura del repositorio

```
.
├── backend/              # ASP.NET Core Web API
├── frontend/             # Next.js + TypeScript
├── sync-agent/           # Agente de sincronización con Aspel SAE (solo lectura)
├── docs/                 # Documentación técnica
├── .github/
│   ├── workflows/ci.yml  # CI: lint, build y pruebas
│   └── pull_request_template.md
├── docker-compose.yml    # PostgreSQL para desarrollo
└── .env.example          # Plantilla de variables de entorno
```

## Requisitos

- [Git](https://git-scm.com/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (incluido con Visual Studio 2026). El agente de sincronización (`sync-agent/`) **requiere** el SDK 10: lo fija su `global.json`. Si no lo tiene, instálelo con `winget install --id Microsoft.DotNet.SDK.10 -e`.
- [Node.js](https://nodejs.org/) 22 o superior (ver `engines` en `frontend/package.json`)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)

## Puesta en marcha

### 1. Clonar y configurar variables

```powershell
git clone https://github.com/MirhallM/DesarrolloWeb-ProjectoVinculacion.git
cd DesarrolloWeb-ProjectoVinculacion
Copy-Item .env.example .env
```

Edita `.env` y cambia, como mínimo, `POSTGRES_PASSWORD`. El archivo `.env` no se sube al repositorio.

### 2. Base de datos (PostgreSQL)

```powershell
docker compose up -d
docker compose ps          # el estado debe ser "healthy"
docker compose exec postgres-db psql -U admin -d nettes_db -c "SELECT version();"
```

Usa en el último comando el usuario y la base que pusiste en `.env`.

| Comando | Efecto |
|---|---|
| `docker compose down` | Apaga la base de datos y **conserva** los datos |
| `docker compose down -v` | Apaga la base y **borra** los datos |
| `docker compose logs postgres-db` | Muestra los registros del contenedor |

> Si cambias `POSTGRES_PASSWORD` después del primer arranque, PostgreSQL no lo actualiza: hay que recrear el volumen con `docker compose down -v`.

**SQL Server de prueba (solo para el agente de sincronización).** `docker compose up -d` sigue levantando únicamente PostgreSQL. Para probar el agente hay un SQL Server 2022 que simula la base de Aspel, bajo el perfil `sqlserver` (escucha solo en `127.0.0.1:14330`):

```powershell
docker compose --profile sqlserver up -d
```

Necesita las variables `MSSQL_*` y `CATALOGO_LECTURA_PASSWORD` de `.env.example`. El backend **no** se conecta a SQL Server. Detalles en [`sync-agent/README.md`](sync-agent/README.md#4-base-de-prueba-sql-server-en-docker).

### 3. Backend

ASP.NET Core no lee el archivo `.env`. Las cadenas de conexión y las claves se guardan con User Secrets, por cada integrante:

```powershell
cd backend/src/backend
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=nettes_db;Username=admin;Password=TU_PASSWORD"
dotnet run
```

Para cargar las categorías y reglas del catálogo en tu base (después de aplicar las migraciones con `dotnet ef database update`):

```powershell
dotnet run -- seed
```

Se puede repetir sin problema; deja las tablas iguales a `Data/Seed/categories.json`. En Railway se ejecuta como comando de pre-deploy: `dotnet backend.dll seed`.

La contraseña debe ser la misma que `POSTGRES_PASSWORD`. La URL del API aparece en la consola al iniciar. Las demás variables que se necesitarán más adelante (`Jwt:*`) están documentadas en `.env.example`. La conexión a Aspel SAE **no** va en el backend: la usa únicamente el agente de sincronización (ver [`sync-agent/README.md`](sync-agent/README.md)).

### 4. Frontend

```powershell
cd frontend
npm install
npm run dev
```

La aplicación queda disponible en http://localhost:3000.

## Flujo de trabajo

El trabajo se organiza en [GitHub Projects](https://github.com/users/MirhallM/projects/3) con épicas, historias de usuario (`US-01` a `US-22`) y sprints semanales.

1. Toma una historia y crea una rama desde `main`: `feature/US-05-conexion-sqlserver`.
2. Haz commits pequeños con prefijos: `feat:`, `fix:`, `docs:`, `chore:`, `test:`.
3. Abre un pull request hacia `main` y usa la plantilla. Enlaza el issue con `Closes #N` cuando el PR complete la historia, o con `Refs #N` si solo avanza una parte.
4. El otro integrante revisa. El CI debe pasar (`frontend` y `backend`).
5. Se hace merge con squash. El título del PR queda como mensaje del commit en `main`.

Las reglas de calidad están en la **Definition of Done** de la plantilla de PR. En particular: **nunca se suben contraseñas ni credenciales al repositorio**.

## Planificación

| Sprint | Enfoque |
|---|---|
| Semana 3 | Arquitectura, entornos de desarrollo, esquema de PostgreSQL y reglas del negocio |
| Semana 4 | Conexión de solo lectura a SQL Server, importación, sincronización automática y endpoints |
| Semana 5 | Interfaz del catálogo, búsqueda, filtros y fecha de actualización de existencias |
| Semana 6 | Autenticación, roles y favoritos |
| Semana 7 | Carga de imágenes, panel administrativo, contenido promocional y destacados |
| Semana 8 | Pruebas de integración, seguridad y respaldos |
| Semana 9 | Despliegue en la VM y documentación |
| Semana 10 | Margen para correcciones, demostración y entrega académica |

La planificación puede ajustarse según el avance. Las vistas del Project están documentadas en [`docs/project-views.md`](docs/project-views.md).

## Seguridad

- Las operaciones administrativas se validan en el backend, no solo en la interfaz.
- Las contraseñas se almacenan con hash seguro (ASP.NET Core Identity), nunca en texto plano.
- La conexión a Aspel SAE usa una cuenta de SQL Server con permisos de solo lectura.
- Los puertos de PostgreSQL y SQL Server no se exponen a Internet.
- Se respaldan la base de datos de la aplicación y la carpeta de imágenes.

## Equipo

Marcelo Rafael Molina Sierra - 21911259

Josue Manuel Gavidia Gonzalez - 22241334
