# Despliegue en Railway

Esqueleto de la aplicación en Railway (#47): frontend, backend y PostgreSQL en un mismo proyecto. Lo de producción final (dominio propio, respaldos, monitoreo) queda en US-21 (#29). La arquitectura está en [architecture.md](architecture.md).

## 1. Servicios

| Servicio | Origen | Acceso | Notas |
|---|---|---|---|
| `frontend` | Rama `main`, Root Directory `frontend` | **Único dominio público** (`*.up.railway.app`) | Next.js. Reenvía todo `/api/*` al backend por la red privada. |
| `backend` | Rama `main`, Root Directory `/backend` | Solo red privada (`backend.railway.internal`) | ASP.NET Core 10 con [`backend/Dockerfile`](../backend/Dockerfile). Configurado a mano en el panel ([sección 4](#4-pasos-en-el-panel-de-railway-primera-vez)). |
| `Postgres` | Plantilla de PostgreSQL de Railway: **PostgreSQL 18.7**, imagen `ghcr.io/railwayapp-templates/postgres-ssl` | Solo red privada | Sin TCP proxy público. En local se usa `postgres:18`, la misma versión mayor. |

La configuración de los servicios vive **en el panel de Railway**, no en el repositorio. Railway marcó Config as Code (`railway.json`) como obsoleto, así que se quitó `backend/railway.json`. La migración a Infrastructure as Code está en [Pendientes](#7-pendientes).

```
Internet ──HTTPS──► frontend (Next.js) ──/api/* por red privada──► backend (:8080) ──► Postgres
```

## 2. Backend

### Imagen

[`backend/Dockerfile`](../backend/Dockerfile) compila con `dotnet/sdk:10.0` y ejecuta con `dotnet/aspnet:10.0`. Son las etiquetas predeterminadas de .NET 10, basadas en **Ubuntu 24.04**, y traen ICU, que la API necesita para quitar acentos. **No usar Alpine ni chiseled**, porque no lo traen. La imagen corre con el usuario sin privilegios de la imagen oficial y no incluye `tests/`.

### Comandos

El mismo `backend.dll` levanta la API o ejecuta un comando y termina:

| Comando | Qué hace | Quién lo usa |
|---|---|---|
| *(sin argumentos)* | Levanta la API en el puerto `PORT` | Railway, al arrancar |
| `migrate` | Aplica las migraciones pendientes. Si no hay, lo dice y termina con 0. | Pre-deploy |
| `seed` | Siembra categorías y reglas desde `categories.json` | Pre-deploy, después de `migrate` |
| `dp-check protect <texto>` / `dp-check unprotect <protegido>` | Diagnóstico de las llaves de Data Protection ([sección 5](#5-llaves-de-data-protection)) | A mano |

### Pre-deploy y healthcheck

Se configuran en el panel del servicio `backend` ([sección 4](#4-pasos-en-el-panel-de-railway-primera-vez)).

- **Pre-deploy:** `sh -c "dotnet backend.dll migrate && dotnet backend.dll seed"`. Si cualquiera falla (código distinto de 0), Railway **no** pasa a la versión nueva y la anterior sigue en línea.
- **Healthcheck:** `GET /api/health` responde `200 Healthy` si PostgreSQL contesta y `503 Unhealthy` si no. Railway espera hasta 120 s a que responda 200 antes de dar el despliegue por bueno.

### Mensajes esperados en los logs

- `No XML encryptor configured. Key {...} may be persisted to storage in unencrypted form.` aparece al crear una llave de Data Protection. Es esperado; ver [sección 5](#5-llaves-de-data-protection).
- `Failed executing DbCommand ... FROM "__EFMigrationsHistory"` aparece **solo en una base vacía**. EF busca su tabla de historial antes de crearla. Justo después aplica las migraciones.
- `Overriding HTTP_PORTS '8080' ... Binding to values defined by URLS instead 'http://*:8080'` es normal: la imagen trae un puerto predeterminado y `Program.cs` usa el de `PORT`.

## 3. Variables (nombres, sin valores)

Ningún valor va en el repositorio. Se configuran en **Variables** de cada servicio. `${{...}}` son variables de referencia de Railway: se resuelven solas y no hay que copiar contraseñas.

**backend**

| Variable | Valor |
|---|---|
| `ConnectionStrings__DefaultConnection` | `Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}}` |
| `ASPNETCORE_ENVIRONMENT` | `Production` (desactiva Swagger y la página de errores de desarrollo) |
| `PORT` | `8080`. Definirla explícitamente para que el frontend pueda referenciarla. |
| *(US-06)* clave del agente | Se agrega con US-06 (#14). |

`PGHOST` es la dirección **privada** de PostgreSQL. No usar `DATABASE_PUBLIC_URL`.

**frontend**

| Variable | Valor |
|---|---|
| `API_URL` | `http://${{backend.RAILWAY_PRIVATE_DOMAIN}}:${{backend.PORT}}` |

`API_URL` se usa **también al compilar**: Next.js calcula las reglas de reenvío en `next build`. Las páginas son dinámicas (`force-dynamic`), así que la compilación no llama al backend; eso importa porque la red privada no existe durante la compilación.

## 4. Pasos en el panel de Railway (primera vez)

1. Crear el proyecto y conectar el repositorio de GitHub.
2. **Postgres:** agregar *Database → PostgreSQL*. La plantilla crea PostgreSQL 18 con la imagen `ghcr.io/railwayapp-templates/postgres-ssl`. **Sin acceso público:** en *Settings → Networking*, no agregar TCP proxy, y eliminarlo si lo creó.
3. **backend:** *New → GitHub Repo*, rama `main`.

   > **Ojo: nombre del servicio.** Railway le pone al servicio el nombre del repositorio. **Hay que renombrarlo a `backend`** (*Settings → Service Name*), porque las variables de referencia del frontend usan ese nombre (`${{backend.*}}`).

   | Ajuste (*Settings*) | Valor |
   |---|---|
   | *Source → Root Directory* | `/backend` |
   | *Build → Builder* | Dockerfile |
   | *Build → Dockerfile Path* | `/backend/Dockerfile`. Se da desde la raíz del repositorio. |
   | *Build → Watch Paths* | `/backend/**`, para que un cambio solo del frontend no redespliegue el backend |
   | *Deploy → Pre-deploy Command* | `sh -c "dotnet backend.dll migrate && dotnet backend.dll seed"` |
   | *Deploy → Start Command* | **Vacío**: usa el `ENTRYPOINT` del Dockerfile (`dotnet backend.dll`) |
   | *Deploy → Healthcheck Path* | `/api/health` |
   | *Deploy → Healthcheck Timeout* | `120` |
   | *Deploy → Restart Policy* | On Failure, máximo `5` reintentos |
   | *Deploy → Serverless* | Apagado |
   | *Source → Wait for CI* | Encendido: no despliega hasta que pase el CI de GitHub Actions |
   | *Networking* | **Sin** dominio público |
   | *Variables* | `ConnectionStrings__DefaultConnection`, `ASPNETCORE_ENVIRONMENT=Production` y `PORT=8080` (ver [sección 3](#3-variables-nombres-sin-valores)) |

   > **Ojo: no usar "Add Variable" del aviso *Trying to connect a database?*.** Ese botón agrega `DATABASE_URL`, que .NET no usa. La conexión va en `ConnectionStrings__DefaultConnection`, con las referencias `${{Postgres.*}}` de la sección 3.

4. **frontend:** *New → GitHub Repo*, rama `main`. **Renombrarlo a `frontend`**, igual que el backend.

   | Ajuste (*Settings*) | Valor |
   |---|---|
   | *Source → Root Directory* | `/frontend` |
   | *Build → Builder* | Railpack. Detecta Next.js y usa `npm run build` y `npm start`, que respeta `PORT`. |
   | *Build → Watch Paths* | `/frontend/**` |
   | *Source → Wait for CI* | Encendido |
   | *Networking → Generate Domain* | El único dominio público (`*.up.railway.app`) |
   | *Variables* | `API_URL=http://${{backend.RAILWAY_PRIVATE_DOMAIN}}:${{backend.PORT}}` |
5. Desplegar y comprobar:
   - `https://<dominio>.up.railway.app/api/health` debe responder `Healthy`.
   - En los logs del pre-deploy del backend deben aparecer `Migraciones aplicadas.` y el resumen de la siembra.
   - El backend y Postgres no deben tener dominio ni proxy público.
6. Una semana después: revisar el consumo (presupuesto: menos de $20 al mes).

## 5. Llaves de Data Protection

ASP.NET Core cifra las cookies de Identity con llaves de Data Protection. Por defecto se guardan en el disco del contenedor, y **cada despliegue de Railway arranca un contenedor nuevo**: se perderían las llaves y todas las sesiones quedarían inválidas.

Por eso se guardan en PostgreSQL, en la tabla `data_protection_keys`:
- `PersistKeysToDbContext<AppDbContext>()`, con la migración `AddDataProtectionKeys`.
- `SetApplicationName("nettes-backend")`, para que no dependan de la ruta de la aplicación.

**Cómo comprobarlo:** contenedores nuevos, no `docker restart`, porque este conserva el disco del contenedor.

```bash
# Base desechable "deploy_check" en el PostgreSQL de docker compose; la cadena va en un archivo fuera del repo.
docker build -t nettes-backend backend
docker run --rm --network nettes_default --env-file <archivo>.env nettes-backend migrate

docker run -d --name dp-a --network nettes_default -p 18080:8080 --env-file <archivo>.env nettes-backend
#   -> anotar: SELECT id, friendly_name FROM data_protection_keys;
P=$(docker run --rm --network nettes_default --env-file <archivo>.env nettes-backend dp-check protect "hola-nettes")
docker rm -f dp-a

docker run -d --name dp-b --network nettes_default -p 18080:8080 --env-file <archivo>.env nettes-backend
#   -> mismas filas en data_protection_keys, sin llaves nuevas
docker run --rm --network nettes_default --env-file <archivo>.env nettes-backend dp-check unprotect "$P"   # imprime hola-nettes
# Control: contra una base vacía recién migrada, el mismo unprotect falla con código 1.
```

Resultado del 2026-11-23:
- A creó 1 llave.
- B, un contenedor nuevo, vio exactamente la misma fila.
- `unprotect` devolvió `hola-nettes`.
- El control negativo falló con código 1.

La prueba de integración `DeploymentIntegrationTests` cubre lo mismo con dos hosts.

## 6. Redesplegar

- **Normal:** un push o merge a `main` despliega automáticamente los servicios cuyos *Watch Paths* cambiaron.
- **A mano:** en el servicio, *Deployments → Redeploy*.
- **Volver a una versión anterior:** *Deployments*, elegir el despliegue bueno, *Rollback*. Las migraciones **no** se revierten solas: si una migración ya aplicada rompe algo, se corrige con una migración nueva.
- **Migración que falla:** el pre-deploy termina con código 1, el despliegue se marca como fallido y la versión anterior sigue en línea.

## 7. Pendientes

- **Migrar la configuración a Infrastructure as Code, antes o durante US-21 (#29).** Railway marcó Config as Code como obsoleto: los servicios nuevos ya no pueden usarlo, y los archivos `railway.json` / `railway.toml` existentes dejan de leerse el 2026-12-01. Por eso hoy todo se configura a mano en el panel ([sección 4](#4-pasos-en-el-panel-de-railway-primera-vez)). El reemplazo es un archivo `.railway/railway.ts` que maneja la CLI (`railway config init`, `railway config pull` para partir de lo que ya está en el panel, `railway config plan` y `railway config apply`). Guía: <https://docs.railway.com/infrastructure-as-code>.
- **Forwarded headers.** El backend está detrás del proxy de Railway y del reenvío de Next.js, así que hoy ve HTTP y la IP del frontend, no la del usuario. Hay que configurar `UseForwardedHeaders` (`X-Forwarded-For` y `X-Forwarded-Proto`), confiando solo en la red privada, **antes de**:
  - las historias de login y roles (#21 y #22): cookies `Secure` y esquema HTTPS correctos;
  - el rate limiting (#48): limitar por la IP real del cliente y no por la del frontend.
- **Tamaño del cuerpo en el reenvío.** Revisar el límite de tamaño del cuerpo de las peticiones en el reenvío `/api/*` de Next.js y en Kestrel (30 MB por defecto) antes de los lotes de productos de US-06 (#14). Definir el tamaño de lote para quedar bien por debajo.
- **Cifrado de las llaves en reposo.** Las llaves de Data Protection se guardan sin cifrar en PostgreSQL, y quien lea la base podría falsificar cookies. Es aceptable mientras PostgreSQL solo sea accesible por la red privada. Evaluar `ProtectKeysWithCertificate` antes de US-21.
- **Varias réplicas.** Si dos contenedores arrancan a la vez contra una base sin llaves, cada uno puede crear la suya. No es un error: ambas quedan en la tabla y las dos sirven.
