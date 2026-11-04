# Esquema de base de datos (PostgreSQL)

**Catálogo Web – Mini Super Nettes**  
Documento de la historia **US-03: Diseñar el esquema de PostgreSQL**.

> Las reglas de Aspel SAE que afectan este esquema están en [`business-rules.md`](business-rules.md) (US-04).

## 1. Decisiones de diseño

| Tema | Decisión |
|---|---|
| Usuarios | ASP.NET Core Identity, con llaves `uuid` y tablas en `snake_case` (`users`, `roles`, `user_roles`…) |
| Llave de `products` | Llave propia `uuid`; `code` (el código de artículo de Aspel) es único |
| Productos que dejan de venir de Aspel | Se conservan con `is_active = false`; no se borran |
| Imágenes | Una sola imagen por producto; reemplazarla actualiza la misma fila |
| Contenido del sitio | Dos tipos: `banner` y `promotion` |
| Datos de registro | Solo correo y contraseña (sin nombre) |
| Nombres | Tablas en plural y columnas en `snake_case` |
| Fechas | `timestamptz`, guardadas en UTC |
| Estados y tipos | Texto con restricción `CHECK` (no enums nativos de PostgreSQL), para facilitar las migraciones |
| Categorías | Propias de la aplicación, asignadas con reglas de palabras clave; Aspel no tiene categorías útiles |
| Nombre para el cliente | `display_name`, generado a partir de `description`; Aspel no se modifica |

## 2. Diagrama de relaciones

```mermaid
erDiagram
    users ||--o{ user_roles : "tiene"
    roles ||--o{ user_roles : "asigna"
    users ||--o{ favorites : "guarda"
    products ||--o{ favorites : "es favorito"
    products ||--o| product_images : "tiene una"
    products ||--o| featured_products : "puede ser"
    categories ||--o{ products : "agrupa"
    categories ||--o{ category_rules : "se asigna con"
    users |o--o{ product_images : "sube"
    users |o--o{ site_content : "edita"

    users {
        uuid id PK
        varchar user_name "igual al correo"
        varchar normalized_user_name UK
        varchar email
        varchar normalized_email
        boolean email_confirmed
        text password_hash "hash"
        int access_failed_count
        timestamptz lockout_end
        boolean is_active "propio"
        timestamptz created_at "propio"
    }
    roles {
        uuid id PK
        varchar name "User o Admin"
        varchar normalized_name UK
    }
    user_roles {
        uuid user_id PK, FK
        uuid role_id PK, FK
    }
    products {
        uuid id PK
        varchar code UK "CVE_ART de Aspel"
        varchar description "original de Aspel"
        varchar display_name "nombre para el cliente"
        uuid category_id FK
        numeric public_price "12,2 y mayor o igual a 0"
        numeric stock "puede ser negativo"
        boolean is_active "false si sale de Aspel"
        timestamptz synced_at
        timestamptz created_at
        timestamptz updated_at
    }
    categories {
        uuid id PK
        varchar name UK
        varchar slug UK
        int position "mayor o igual a 1"
        timestamptz created_at
        timestamptz updated_at
    }
    category_rules {
        uuid id PK
        uuid category_id FK
        varchar keyword UK "mayusculas y sin acentos"
        int priority
        timestamptz created_at
        timestamptz updated_at
    }
    favorites {
        uuid user_id PK, FK
        uuid product_id PK, FK
        timestamptz created_at
    }
    product_images {
        uuid id PK
        uuid product_id FK, UK "una por producto"
        text image_path "ruta en el volumen"
        varchar alt_text
        varchar mime_type
        bigint file_size_bytes
        uuid uploaded_by FK
        timestamptz created_at
        timestamptz updated_at
    }
    featured_products {
        uuid id PK
        uuid product_id FK, UK
        int position "unica entre activos"
        boolean is_active
        timestamptz created_at
        timestamptz updated_at
    }
    site_content {
        uuid id PK
        varchar content_type "banner o promotion"
        varchar title
        text body
        text image_path
        boolean is_active
        timestamptz start_at
        timestamptz end_at
        uuid updated_by FK
        timestamptz created_at
        timestamptz updated_at
    }
    sync_runs {
        uuid id PK
        varchar status "running, succeeded, failed"
        timestamptz started_at
        timestamptz finished_at
        int products_read
        int products_inserted
        int products_updated
        int products_deactivated
        text error_message
    }
```

`sync_runs` no se relaciona con otras tablas: es un registro independiente. El diagrama omite las tablas de apoyo de Identity (`user_claims`, `role_claims`, `user_logins`, `user_tokens`) y algunas columnas internas de `users`; el modelo completo está en [`database-schema.dbml`](database-schema.dbml), que se puede abrir en dbdiagram.io.

## 3. Tablas de ASP.NET Core Identity

Las gestiona Identity y se crean con la migración inicial. Se renombran a `snake_case` y usan `uuid` como llave.

| Tabla | Contenido |
|---|---|
| `users` | Cuentas de usuario |
| `roles` | Roles (`User` y `Admin`) |
| `user_roles` | Relación usuario-rol |
| `user_claims`, `role_claims`, `user_logins`, `user_tokens` | Tablas de apoyo de Identity (se crean aunque no se usen todas) |

Según la versión de Identity, la migración puede incluir tablas adicionales (por ejemplo, para llaves de acceso); se revisan al generarla.

### `users`

| Columna | Tipo | Notas |
|---|---|---|
| `id` | `uuid` | Llave primaria |
| `user_name` | `varchar(256)` | Igual al correo |
| `normalized_user_name` | `varchar(256)` | Índice único |
| `email` | `varchar(256)` | |
| `normalized_email` | `varchar(256)` | Índice |
| `email_confirmed` | `boolean` | |
| `password_hash` | `text` | Hash; nunca la contraseña |
| `security_stamp`, `concurrency_stamp` | `text` | Los usa Identity |
| `phone_number`, `phone_number_confirmed`, `two_factor_enabled` | | Campos estándar; no se usan en el proyecto |
| `lockout_end`, `lockout_enabled`, `access_failed_count` | | Bloqueo tras intentos fallidos |
| `is_active` | `boolean` | Campo propio. Por defecto `true` |
| `created_at` | `timestamptz` | Campo propio |

El registro solo pide correo y contraseña. No se guarda nombre.

### `roles`

Roles iniciales sembrados en la migración: `User` y `Admin`. La primera cuenta administradora **no** se crea aquí; se hace con un proceso controlado (ver `architecture.md`).

## 4. Tablas del proyecto

### `products`

Copia sincronizada del catálogo de Aspel SAE. Solo la modifica el proceso de sincronización.

| Columna | Tipo | Restricciones / notas |
|---|---|---|
| `id` | `uuid` | Llave primaria |
| `code` | `varchar(50)` | `NOT NULL`, **único**. Viene de Aspel (`CVE_ART`) |
| `description` | `varchar(255)` | `NOT NULL`. Descripción original de Aspel; no se muestra al cliente |
| `display_name` | `varchar(255)` | `NOT NULL`. Nombre para mostrar al cliente, generado a partir de `description` |
| `category_id` | `uuid` | `NOT NULL`, FK a `categories(id)`, `ON DELETE RESTRICT` |
| `public_price` | `numeric(12,2)` | `NOT NULL`, `CHECK (public_price >= 0)`. Precio con `CVE_PRECIO = 1`; se guarda `0` si no existe |
| `stock` | `numeric(14,4)` | `NOT NULL`. Sin `CHECK`: Aspel puede reportar existencias negativas |
| `is_active` | `boolean` | `NOT NULL`, por defecto `true`. `STATUS = 'A'` es `true`; `STATUS = 'B'` o dejar de venir de Aspel es `false` |
| `synced_at` | `timestamptz` | `NOT NULL`. Última vez que se sincronizó |
| `created_at`, `updated_at` | `timestamptz` | `NOT NULL` |

Índices:

- Único sobre `code`.
- `(is_active, category_id)`, para los filtros del catálogo.
- `category_id`, creado por EF Core para la llave foránea.
- GIN con `pg_trgm` sobre `display_name`, para la búsqueda por nombre. Hay que confirmar que la extensión esté disponible en la base de datos de Railway.

Visibilidad: `is_active = true` y `public_price > 0`. Los productos visibles con `stock > 0` se muestran como Disponible y los demás como Agotado. El detalle está en [`business-rules.md`](business-rules.md).

### `categories`

Categorías propias del catálogo.

| Columna | Tipo | Restricciones / notas |
|---|---|---|
| `id` | `uuid` | Llave primaria |
| `name` | `varchar(100)` | `NOT NULL`, **único** |
| `slug` | `varchar(100)` | `NOT NULL`, **único** |
| `position` | `integer` | `NOT NULL`, `CHECK (position >= 1)` |
| `created_at`, `updated_at` | `timestamptz` | `NOT NULL` |

Semilla: la categoría **Otros** (`otros`, posición 1000), para los productos que no coinciden con ninguna regla. Su id está disponible en C# como `Category.OthersId`.

### `category_rules`

Reglas de palabras clave para categorizar productos. Las mantiene el equipo; todavía no tienen datos semilla.

| Columna | Tipo | Restricciones / notas |
|---|---|---|
| `id` | `uuid` | Llave primaria |
| `category_id` | `uuid` | `NOT NULL`, FK a `categories(id)`, `ON DELETE CASCADE` |
| `keyword` | `varchar(100)` | `NOT NULL`, **único**. Se guarda en mayúsculas y sin acentos |
| `priority` | `integer` | `NOT NULL`. Por defecto `0`, asignado en C# |
| `created_at`, `updated_at` | `timestamptz` | `NOT NULL` |

Índice sobre `category_id`. Si varias reglas coinciden, gana la de mayor prioridad y, si empatan, la palabra clave más larga.

### `favorites`

| Columna | Tipo | Restricciones / notas |
|---|---|---|
| `user_id` | `uuid` | FK a `users(id)`, `ON DELETE CASCADE` |
| `product_id` | `uuid` | FK a `products(id)`, `ON DELETE RESTRICT` |
| `created_at` | `timestamptz` | `NOT NULL` |

Llave primaria compuesta `(user_id, product_id)`: impide duplicados. Índice sobre `product_id`.

### `product_images`

| Columna | Tipo | Restricciones / notas |
|---|---|---|
| `id` | `uuid` | Llave primaria |
| `product_id` | `uuid` | `NOT NULL`, **único**, FK a `products(id)`, `ON DELETE CASCADE` |
| `image_path` | `text` | `NOT NULL`. Ruta del archivo en el volumen |
| `alt_text` | `varchar(255)` | Texto alternativo |
| `mime_type` | `varchar(100)` | `NOT NULL` |
| `file_size_bytes` | `bigint` | `NOT NULL`, `CHECK (file_size_bytes > 0)` |
| `uploaded_by` | `uuid` | FK a `users(id)`, `ON DELETE SET NULL` |
| `created_at`, `updated_at` | `timestamptz` | `NOT NULL` |

El archivo vive en el volumen de Railway; la base solo guarda la ruta y los metadatos.

### `featured_products`

| Columna | Tipo | Restricciones / notas |
|---|---|---|
| `id` | `uuid` | Llave primaria |
| `product_id` | `uuid` | `NOT NULL`, **único**, FK a `products(id)`, `ON DELETE CASCADE` |
| `position` | `integer` | `NOT NULL`, `CHECK (position >= 1)` |
| `is_active` | `boolean` | `NOT NULL`, por defecto `true` |
| `created_at`, `updated_at` | `timestamptz` | `NOT NULL` |

Índice único parcial sobre `position` donde `is_active = true`, para que dos destacados activos no compartan posición.

### `site_content`

| Columna | Tipo | Restricciones / notas |
|---|---|---|
| `id` | `uuid` | Llave primaria |
| `content_type` | `varchar(50)` | `NOT NULL`, `CHECK (content_type IN ('banner', 'promotion'))` |
| `title` | `varchar(200)` | `NOT NULL` |
| `body` | `text` | Texto de la promoción |
| `image_path` | `text` | Ruta de la imagen |
| `is_active` | `boolean` | `NOT NULL`, por defecto `true` |
| `start_at`, `end_at` | `timestamptz` | Vigencia. `CHECK (end_at IS NULL OR start_at IS NULL OR end_at > start_at)` |
| `updated_by` | `uuid` | FK a `users(id)`, `ON DELETE SET NULL` |
| `created_at`, `updated_at` | `timestamptz` | `NOT NULL` |

Índice sobre `(is_active, content_type, start_at, end_at)`. Qué campos son obligatorios según el tipo (por ejemplo, la vigencia de una promoción) se valida en el backend.

### `sync_runs`

Un registro por ejecución de la sincronización.

| Columna | Tipo | Restricciones / notas |
|---|---|---|
| `id` | `uuid` | Llave primaria |
| `status` | `varchar(20)` | `NOT NULL`, `CHECK (status IN ('running', 'succeeded', 'failed'))` |
| `started_at` | `timestamptz` | `NOT NULL` |
| `finished_at` | `timestamptz` | `CHECK (finished_at IS NULL OR finished_at >= started_at)` |
| `products_read` | `integer` | `NOT NULL`, por defecto 0 |
| `products_inserted` | `integer` | `NOT NULL`, por defecto 0 |
| `products_updated` | `integer` | `NOT NULL`, por defecto 0 |
| `products_deactivated` | `integer` | `NOT NULL`, por defecto 0. Productos que pasan a `is_active = false` |
| `error_message` | `text` | Resumen del error, sin datos sensibles |

Índices:

- Único parcial sobre `status` donde `status = 'running'`: la base impide dos sincronizaciones en ejecución a la vez.
- `(status, finished_at DESC)`, para obtener la última sincronización exitosa (US-12).

## 5. Fuera de este esquema

El área de preparación de los lotes de sincronización se define en US-06 y US-07, cuando se implemente el endpoint que los recibe. Si requiere tablas, se agregan con una migración propia.

## 6. Implementación con Entity Framework Core

### Código

| Archivo | Contenido |
|---|---|
| `backend/Data/AppDbContext.cs` | Hereda de `IdentityDbContext<AppUser, AppRole, Guid>`. Configura tablas, llaves, índices, restricciones y la semilla de roles y categorías. Llena `created_at` y `updated_at` al guardar. |
| `backend/Data/SnakeCaseNaming.cs` | Convierte a `snake_case` tablas, columnas, llaves e índices, y quita el prefijo `AspNet` de las tablas de Identity. |
| `backend/Data/Entities/` | Entidades: `AppUser`, `AppRole`, `Category`, `CategoryRule`, `Product`, `Favorite`, `ProductImage`, `FeaturedProduct`, `SiteContent`, `SyncRun`. |
| `backend/Data/Migrations/` | Migraciones generadas con `dotnet ef`. |

Notas:

- Los nombres en `snake_case` se aplican con un helper propio, sin paquetes adicionales.
- Los valores por defecto de los `boolean` (`is_active = true`) se asignan en C#, no en la base de datos: con un valor por defecto en la base, EF Core no enviaría `false` al insertar.
- Los tipos y estados (`banner`, `promotion`, `running`…) son constantes de texto (`SiteContentTypes`, `SyncRunStatuses`) validadas con `CHECK`.

### Paquetes

`Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore` y `Microsoft.EntityFrameworkCore.Design`, en la versión 10.0.x.

### Crear la base desde cero

```powershell
dotnet tool install --global dotnet-ef --version "10.*"
docker compose up -d
cd backend
dotnet ef migrations add InitialCreate --output-dir Data/Migrations   # solo la primera vez
dotnet ef database update
```

`database update` usa la cadena `ConnectionStrings:DefaultConnection` de User Secrets (ver README).

Para verificar que las migraciones crean todo desde cero:

```powershell
docker compose down -v
docker compose up -d
cd backend
dotnet ef database update
docker compose exec postgres-db psql -U admin -d nettes_db -c "\dt"
```

### Cambios futuros al esquema

1. Modificar las entidades o `AppDbContext`.
2. `dotnet ef migrations add <NombreDescriptivo> --output-dir Data/Migrations`.
3. Revisar el archivo generado antes de confirmarlo.
4. `dotnet ef database update`.

Nunca se edita una migración que ya está en `main`; se crea una nueva.

## 7. Pendientes

- Confirmar que `pg_trgm` se puede activar en PostgreSQL de Railway (es una extensión de confianza desde PostgreSQL 13).
- Estrategia de preparación de lotes de sincronización (US-06 y US-07).
