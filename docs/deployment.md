# Despliegue en Railway

Esqueleto de la aplicación en Railway (#47): frontend, backend y PostgreSQL en un mismo proyecto. Lo de producción final (dominio propio, respaldos, monitoreo) queda en US-21 (#29). La arquitectura está en [architecture.md](architecture.md).

## 1. Servicios

| Servicio | Origen | Acceso | Notas |
|---|---|---|---|
| `frontend` | Rama `main`, Root Directory `frontend` | **Único dominio público** (`*.up.railway.app`) | Next.js. Reenvía todo `/api/*` al backend por la red privada. |
| `backend` | Rama `main`, Root Directory `/backend` | Solo red privada (`backend.railway.internal`) | ASP.NET Core 10 con [`backend/Dockerfile`](../backend/Dockerfile). Configurado a mano en el panel ([sección 4](#4-pasos-en-el-panel-de-railway-primera-vez)). |
| `Postgres` | Plantilla de PostgreSQL de Railway: **PostgreSQL 18.7**, imagen `ghcr.io/railwayapp-templates/postgres-ssl` | Solo red privada | Sin TCP proxy público. En local se usa `postgres:18`, la misma versión mayor. |

La configuración de los servicios vive **en el panel de Railway**, no en el repositorio. Railway marcó Config as Code (`railway.json`) como obsoleto, así que se quitó `backend/railway.json`. La migración a Infrastructure as Code está en [Pendientes](#9-pendientes).

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
| `seed` | Siembra categorías y reglas desde `categories.json` y **recategoriza todos los productos** (y recalcula su nombre) con las reglas y el diccionario del despliegue. Espera hasta 60 s a una sincronización en curso | Pre-deploy, después de `migrate` |
| `dp-check protect <texto>` / `dp-check unprotect <protegido>` | Diagnóstico de las llaves de Data Protection ([sección 5](#5-llaves-de-data-protection)) | A mano |

### Pre-deploy y healthcheck

Se configuran en el panel del servicio `backend` ([sección 4](#4-pasos-en-el-panel-de-railway-primera-vez)).

- **Pre-deploy:** `sh -c "dotnet backend.dll migrate && dotnet backend.dll seed"`. Si cualquiera falla (código distinto de 0), Railway **no** pasa a la versión nueva y la anterior sigue en línea.
- **Qué hace `seed` con los productos:** compara la categoría y el nombre de cada producto con lo que dan las reglas del despliegue y escribe solo los que difieren. El resumen del log dice cuántos cambiaron (`Productos: N con otra categoría, M con otro nombre`). En un despliegue sin cambios de reglas son `0` y `0`. Después del despliegue, la primera corrida del agente es completa aunque el inventario no haya cambiado (la huella de las reglas incluye `RAILWAY_GIT_COMMIT_SHA`, que Railway define solo): es lo esperado ([architecture.md](architecture.md#51-protocolo-us-06)).
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
| `Sync__AgentKeyHashes` | Hash SHA-256 (hexadecimal, 64 caracteres) de la llave del agente. **No es la llave**: la llave solo vive en el servidor de la tienda. Se genera con `sync-agent/scripts/New-SyncAgentKey.ps1` (ver abajo). Admite dos hashes separados por coma para rotar la llave (procedimiento en `sync-agent/README.md`, paso 7). |
| `Sync__MaxDeactivationRatio` | *Opcional.* Freno de seguridad, por defecto `0.10`. Solo se sube **temporalmente** si se eliminaron de verdad muchos artículos de Aspel; después se borra la variable. |
| `Sync__StatusDeactivationWarningCount` | *Opcional.* Por defecto `100`. Desde esa cantidad de productos que pasan de activo a baja por `STATUS` en una corrida, el backend escribe una advertencia en el log (no frena nada). `0` apaga el aviso. |

`PGHOST` es la dirección **privada** de PostgreSQL. No usar `DATABASE_PUBLIC_URL`.

**Llave del agente (US-06).** Se genera **en el servidor de la tienda**, en una consola de administrador, después de instalar el agente:

```powershell
.\New-SyncAgentKey.ps1 -ConfigPath C:\NettesSyncAgent\appsettings.Production.json
```

El script escribe la llave en `Api.AgentKey` de ese archivo (que solo leen Administradores, SYSTEM y la cuenta del agente) y muestra **solo el hash**, que se pega en `Sync__AgentKeyHashes`. La llave nunca pasa por la línea de comandos ni por el chat. Sin la variable, el backend arranca igual, avisa en el log y responde `401` en `/api/sync`. Las demás opciones de `Sync__*` tienen valores por defecto (ver `backend/README.md`).

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
   | *Variables* | `ConnectionStrings__DefaultConnection`, `ASPNETCORE_ENVIRONMENT=Production`, `PORT=8080` y, cuando se instale el agente, `Sync__AgentKeyHashes` (ver [sección 3](#3-variables-nombres-sin-valores)) |

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
   - En los logs del pre-deploy del backend deben aparecer `Migraciones aplicadas.` y el resumen de la siembra, que incluye cuántos productos cambiaron de categoría o de nombre.
   - El backend y Postgres no deben tener dominio ni proxy público.
6. Una semana después: revisar el consumo (presupuesto: menos de $20 al mes).

## 5. Llaves de Data Protection

ASP.NET Core cifra la cookie de sesión de Identity (`__Host-nettes.auth` en producción; ver [architecture.md](architecture.md#6-autenticación-y-autorización)) con llaves de Data Protection. Por defecto se guardan en el disco del contenedor, y **cada despliegue de Railway arranca un contenedor nuevo**: se perderían las llaves y todas las sesiones quedarían inválidas.

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

## 7. Límites del reenvío para la sincronización

Revisado con Next.js 16.4.6 (la versión instalada) para los lotes de US-06:

| Límite | Valor | Qué pasa si se supera |
|---|---|---|
| Cuerpo en el reenvío `/api/*` (`experimental.proxyClientMaxBodySize`) | 10 MB por defecto | Next.js escribe un aviso en el log (*«Request body exceeded 10MB… Only the first 10MB will be available»*) y el cliente recibe **`500`**; el backend no recibe el cuerpo completo. Medido con Next.js 16.4.6: cuerpos de 6.5 y 8.6 MB pasan íntegros con el encabezado de la llave; uno de 14 MB da `500`. |
| Tiempo de espera del reenvío (`experimental.proxyTimeout`) | 30 s por defecto | El agente recibe un error, aunque el backend siga trabajando. |
| Kestrel | 30 MB | `413`. |
| `POST /api/sync/runs/{id}/batches` | 4 MB (`[RequestSizeLimit]`) | `413`. |

Decisiones:

- Lotes de **1000 artículos** (unos 106 KB medidos con datos parecidos a los reales; 213 KB con 2000), con un máximo de 2000 en el servidor: muy por debajo de los 10 MB. El inventario completo (unos 18 700 artículos) viaja en unos 19 lotes, nunca en una sola solicitud. No hace falta cambiar `next.config.ts`.\n- El encabezado `X-Sync-Agent-Key` llega al backend por el reenvío (se comprobó con un servidor eco detrás de la misma regla `rewrites`). El backend no tiene dominio público: el agente entra siempre por el dominio del frontend.
- **Completar** aplica unos 18 500 artículos en **5 a 9 s** en un portátil, según la carga de la máquina (prueba `Carga_de_18500_articulos_registra_cuanto_tarda_completar`), por debajo de los 30 s. Si alguna vez tarda más, no se pierde: la transacción no depende de la conexión del cliente y el reintento del agente recibe el resumen ya confirmado.

## 8. Traspaso del proyecto a la cuenta de la tienda

Al terminar el proyecto, Railway debe quedar a nombre de la tienda: con su cuenta y su medio de pago, sin accesos del equipo de desarrollo. Quién abre y paga esa cuenta sigue pendiente de definir (ver [architecture.md](architecture.md)). Railway ofrece transferir un proyecto a otro espacio de trabajo (*Settings* del proyecto); los requisitos de plan cambian, así que **confírmelos en el panel con tiempo**, antes de la fecha de entrega. Lo que importa es que se **transfiera** el proyecto y no que se recree: así se conservan los servicios y sus datos.

**Antes de transferir**, anote: el `totalCount` de `/api/products?pageSize=1`, el dominio público y los nombres de las variables de cada servicio ([sección 3](#3-variables-nombres-sin-valores)).

### Qué se conserva

- Los servicios `frontend`, `backend` y `Postgres`, con el volumen de PostgreSQL y sus datos.
- Las variables de cada servicio, entre ellas `Sync__AgentKeyHashes` (el hash de la llave del agente), `ConnectionStrings__DefaultConnection`, `ASPNETCORE_ENVIRONMENT`, `PORT` y `API_URL`.
- El dominio público del `frontend` (`*.up.railway.app`). El agente de la tienda lo tiene en `Api:BaseUrl`: **si el dominio cambia** (por ejemplo, si se recrearon los servicios), hay que actualizarlo en el servidor con `Set-AgentConnection.ps1 -BaseUrl https://<dominio nuevo>` (vuelve a pedir la contraseña de `catalogo_lectura`).
- La conexión con el repositorio de GitHub (rama `main`, *Root Directory*, *Watch Paths* y *Wait for CI*, [sección 4](#4-pasos-en-el-panel-de-railway-primera-vez)). Si se pierde, se reconecta.

### Qué se comprueba después

1. `https://<dominio>/api/health` responde `Healthy` y `https://<dominio>/api/sync/ping` responde `401`.
2. `/api/products?pageSize=1` da el mismo `totalCount` que se anotó antes.
3. **En el servidor de la tienda:** `& 'C:\NettesSyncAgent\SyncAgent.exe' --test` dice «Responde y la llave es válida». Después, la tarea termina con `LastTaskResult : 0` y `lastPublish` en `unchanged` ([sync-agent/README.md](../sync-agent/README.md), paso 8).
4. Un despliegue de prueba (*Redeploy* del `backend`) vuelve a quedar `Healthy`.
5. Las alertas y el límite de gasto están configurados en la cuenta nueva (presupuesto de la [sección 4](#4-pasos-en-el-panel-de-railway-primera-vez)).

### Qué se rota o se quita al terminar

Se rota **todo lo que el equipo de desarrollo vio o pudo ver**, y se quitan sus accesos:

- **Accesos del equipo:** quitar a sus integrantes del proyecto de Railway y, si el repositorio pasa al negocio, del repositorio de GitHub. Revocar los tokens personales que hayan creado.
- **La contraseña de PostgreSQL**, visible en las variables del servicio `Postgres` para quien tenía acceso al panel. Rotarla es un procedimiento de **dos partes** que Railway no hace solo: cambiarla en la base (`ALTER USER … PASSWORD`, por ejemplo desde una sesión de `railway connect Postgres`), actualizar las variables de contraseña del servicio `Postgres` (`PGPASSWORD` y `POSTGRES_PASSWORD`) para que coincidan y **redesplegar el `backend`**, que las recibe por referencia. Compruebe `/api/health` al terminar y confirme el procedimiento vigente en la documentación de Railway antes de ejecutarlo.
- **Las cuentas de administrador del sitio** que el equipo haya creado o usado, incluida la primera: el negocio crea las suyas y se desactivan o se les cambia la contraseña a las del equipo.
- **La contraseña de `catalogo_lectura`**, solo si en algún momento la vio alguien del equipo (por ejemplo, durante una sesión remota): `Set-CatalogoLecturaLogin.ps1` y luego `Set-AgentConnection.ps1`.
- **Los secretos de los equipos de desarrollo** (User Secrets y el `.env`) no están en el repositorio ni en Railway; se borran de esos equipos. Las llaves que genera `New-SyncAgentKey.ps1 -Development` solo valían en desarrollo.

### Qué NO se rota ni se guarda en el agente

- **La llave del agente no se rota** por el traspaso: se genera **en el servidor de la tienda** y nunca salió de él. A Railway solo viaja su hash, que no permite reconstruirla. Se rota únicamente si alguna vez se mostró, se copió o se pegó en otro lugar ([rotación](../sync-agent/README.md#7-configurar-la-llave-del-agente)).
- **Un token de la API de Railway nunca va en el agente** ni en el servidor de la tienda ni en el repositorio. El agente solo necesita la URL pública y su propia llave; un token de Railway permite administrar el proyecto entero. Si se creó alguno (por ejemplo, para la CLI o para la migración a Infrastructure as Code, ver Pendientes), vive solo en el equipo de quien administra y se **revoca** al terminar.

## 9. Pendientes

- **Migrar la configuración a Infrastructure as Code, antes o durante US-21 (#29).** Railway marcó Config as Code como obsoleto: los servicios nuevos ya no pueden usarlo, y los archivos `railway.json` / `railway.toml` existentes dejan de leerse el 2026-12-01. Por eso hoy todo se configura a mano en el panel ([sección 4](#4-pasos-en-el-panel-de-railway-primera-vez)). El reemplazo es un archivo `.railway/railway.ts` que maneja la CLI (`railway config init`, `railway config pull` para partir de lo que ya está en el panel, `railway config plan` y `railway config apply`). Guía: <https://docs.railway.com/infrastructure-as-code>.
- **Verificar `X-Forwarded-For` de extremo a extremo antes del rate limiting (#48).** En US-13 (#21) el backend ya configuró `UseForwardedHeaders` (`X-Forwarded-For` y `X-Forwarded-Proto`, `ForwardLimit = 2`, confiando solo en loopback y redes privadas `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16` y `fc00::/7`) y emite cookies `__Host-nettes.auth` con `SecurePolicy.Always` en producción. Antes de activar el *rate limiting* (#48), comprobar en Railway que el reenvío de Next.js propaga la IP del cliente en `X-Forwarded-For` para no limitar por la IP interna del contenedor frontend.
- **Cifrado de las llaves en reposo.** Las llaves de Data Protection se guardan sin cifrar en PostgreSQL, y quien lea la base podría falsificar cookies. Es aceptable mientras PostgreSQL solo sea accesible por la red privada. Evaluar `ProtectKeysWithCertificate` antes de US-21.
- **Varias réplicas.** Si dos contenedores arrancan a la vez contra una base sin llaves, cada uno puede crear la suya. No es un error: ambas quedan en la tabla y las dos sirven.
