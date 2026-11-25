# Agente de sincronización con Aspel SAE (US-05 y US-06)

Programa de .NET 10 que corre en el servidor de la tienda, **lee** la base de Aspel SAE (SQL Server) en modo de solo lectura y **envía** el inventario a la API por HTTPS. Aspel SAE nunca se expone a Internet: el agente es el único que lo consulta, lo hace desde el mismo servidor y solo abre conexiones salientes.

> **Alcance:**
> - **US-05:** leer, resumir y guardar el estado en disco.
> - **US-06:** enviar el inventario **completo** en cada ciclo, con la llave propia del agente. Si no cambió desde la última corrida aplicada (huella SHA-256), la API lo registra como "sin cambios" y no se envían los productos; aun así, al menos una vez al día se envía todo.
> - **US-07 (pendiente):** envío solo de los productos que cambiaron, huellas por producto, aborto en el agente si la lectura se reduce mucho y reconciliación diaria.

El agente entrega los datos **tal como vienen de Aspel**. El redondeo del precio, el nombre para mostrar, la categoría y la visibilidad los aplica la API ([reglas comerciales](../docs/business-rules.md)).

## Contenido

1. [Qué hace y qué no](#1-qué-hace-y-qué-no)
2. [Cómo encaja](#2-cómo-encaja)
3. [Requisitos para desarrollar](#3-requisitos-para-desarrollar)
4. [Base de prueba (SQL Server en Docker)](#4-base-de-prueba-sql-server-en-docker)
5. [Configuración](#5-configuración)
6. [Modos de uso y códigos de salida](#6-modos-de-uso-y-códigos-de-salida)
7. [Pruebas](#7-pruebas)
8. [Publicar](#8-publicar)
9. [Instalación en la tienda](#9-instalación-en-la-tienda)
10. [Contraseñas](#10-contraseñas)
11. [Desinstalación](#11-desinstalación)
12. [Solución de problemas](#12-solución-de-problemas)
13. [Seguridad y criterios de aceptación](#13-seguridad-y-criterios-de-aceptación)
14. [Compatibilidad y riesgos](#14-compatibilidad-y-riesgos)
15. [Qué se probó y qué falta verificar en la tienda](#15-qué-se-probó-y-qué-falta-verificar-en-la-tienda)
16. [Estructura de la carpeta](#16-estructura-de-la-carpeta)

---

## 1. Qué hace y qué no

**Hace**

- Ejecuta una única consulta `SELECT` (`dbo.INVE01` con `LEFT JOIN` a `dbo.PRECIO_X_PROD01` en la lista de precios 1) y recibe las filas en *streaming*, sin ORM y sin transacciones.
- Los artículos **sin precio** en la lista 1 no se pierden (por eso es `LEFT JOIN`): llegan con el precio vacío.
- Entrega `CVE_ART` **sin recortar ni normalizar**: hay códigos que difieren solo por espacios al inicio y son productos distintos.
- Reintenta ante fallos transitorios (red, tiempos de espera, bloqueos) y registra el servidor y la base, **nunca** el usuario ni la contraseña.
- Envía el inventario a la API en lotes de 1000 artículos, con la llave del agente en el encabezado `X-Sync-Agent-Key` ([protocolo](../docs/architecture.md#51-protocolo-us-06)). El catálogo solo cambia cuando la API recibió todo y lo aplicó en una transacción.
- Reintenta los fallos transitorios de la API (red, tiempo de espera, `429`, `5xx`) con esperas de 5, 15 y 45 segundos. Cada paso es idempotente: un reintento no duplica nada.
- Guarda su estado en `data\sync-state.json` y sus registros en `data\logs\` (se borran a los 14 días).
- Tiene un modo de prueba (`--test`) que resume lo leído, mide tiempos y memoria y comprueba que la API responde y acepta la llave, **sin enviar productos ni guardar nada**.

**No hace**

- No escribe en Aspel: la cuenta `catalogo_lectura` solo tiene `SELECT` sobre dos tablas.
- No usa `NOLOCK`: puede devolver filas duplicadas u omitidas. Lee con el aislamiento por defecto (`READ COMMITTED`); con unas 18 mil filas la lectura dura milisegundos.
- No abre ningún puerto: no recibe conexiones.
- No aplica reglas comerciales: el redondeo del precio, el nombre para mostrar, la categoría y la visibilidad los aplica la API.
- No sigue redirecciones HTTP: una redirección podría reenviar la llave a otro destino.
- No escribe la llave en logs, mensajes ni en `sync-state.json`.

## 2. Cómo encaja

```
Servidor de la tienda (Windows Server 2016)                          Internet
┌──────────────────────────────────────────────────────┐
│  Aspel SAE ──► SQL Server (SAE70Empre01)             │
│                      ▲  solo SELECT, cuenta           │
│                      │  catalogo_lectura              │
│  Tarea programada cada 15 min:                        │
│     SyncAgent.exe --once ──► data\sync-state.json     │
│                         └──► data\logs\agent-*.log    │
│                         └──► HTTPS saliente, lotes ───┼──► sitio (Railway) /api/sync
└──────────────────────────────────────────────────────┘
```

**Por qué una tarea programada y no un servicio de Windows.** El servidor tiene solo 4 GB de RAM compartidos con SQL Server, Aspel y los servicios del controlador de dominio.

| | Servicio de Windows | Tarea programada (la elegida) |
|---|---|---|
| Memoria entre ejecuciones | Proceso residente las 24 horas | **0 MB**: el proceso termina |
| Estado entre ejecuciones | En memoria o en disco | En disco (`sync-state.json`), obligatorio |
| Costo de cada arranque | Una vez | Unos 0.3 s de CPU cada 15 minutos (el programa está precompilado) |

La memoria del proceso mientras corre está limitada: recolector de basura de estación de trabajo, sin recolección concurrente y tope de 64 MB de heap (`System.GC.HeapHardLimit`).

## 3. Requisitos para desarrollar

- **SDK de .NET 10.** El repositorio lo fija en [`global.json`](global.json) (versión `10.0.100` con `rollForward: latestFeature`, es decir, toma el 10.0.x más nuevo instalado). Es obligatorio para quien compile o pruebe el agente y para quien **publique** el agente de la tienda.

  ```powershell
  winget install --id Microsoft.DotNet.SDK.10 -e
  dotnet --list-sdks          # debe aparecer un 10.0.x
  ```

  Sin el SDK 10, `dotnet` dentro de `sync-agent\` falla con *«A compatible .NET SDK was not found»*. Puede tener otros SDK instalados (por ejemplo el 11): el resto del repositorio no se ve afectado.
- **Docker Desktop**, para la base de prueba.
- **El servidor de la tienda no necesita SDK ni .NET:** el programa se publica autocontenido.

## 4. Base de prueba (SQL Server en Docker)

Simula la base de Aspel con versiones mínimas de `dbo.INVE01` y `dbo.PRECIO_X_PROD01`. **No arranca con `docker compose up -d`**: hay que pedirla con el perfil `sqlserver`.

1. En el `.env` de la raíz (ver [`.env.example`](../.env.example)) defina `MSSQL_SA_PASSWORD` y `CATALOGO_LECTURA_PASSWORD`. Para la segunda use solo letras, números y `- _ . ! * + @` (ver [Contraseñas](#10-contraseñas)).
2. Levante la base:

   ```powershell
   docker compose --profile sqlserver up -d
   docker compose --profile sqlserver ps -a          # sqlserver: healthy; sqlserver-init: Exited (0)
   docker compose --profile sqlserver logs sqlserver-init
   ```

   `sqlserver-init` crea la base, crea la cuenta con **el mismo script de la tienda**, carga 14 artículos de prueba y comprueba que `catalogo_lectura` puede leer pero que `INSERT`, `UPDATE` y `DELETE` fallan con el error 229. Escucha solo en `127.0.0.1:14330` y usa unos 1 GB de memoria.
3. Los 14 artículos cubren los casos reales: normal, sin precio en la lista 1 (con precio en la 4), de baja, código con y sin espacios al inicio, descripción vacía, `NULL` literal y nula, existencia negativa, con decimales e inflada, precio con decimales de más. El resultado esperado está en el encabezado de [`sql/dev/02-datos-prueba.sql`](sql/dev/02-datos-prueba.sql).

| Para… | Comando |
|---|---|
| Apagarla (conserva datos) | `docker compose --profile sqlserver stop sqlserver` |
| Dejarla como nueva | `docker compose --profile sqlserver run --rm sqlserver-init` |
| Borrarla del todo | `docker compose --profile sqlserver rm -sf sqlserver sqlserver-init` y luego `docker volume rm <volumen que termina en _mssqldata>` (búsquelo con `docker volume ls`) |

> No use `docker compose down -v` para borrar solo SQL Server: también borra el volumen de PostgreSQL.

## 5. Configuración

El agente lee `appsettings.json` (valores de fábrica, sin secretos), luego `appsettings.Production.json` y, en desarrollo, los *User Secrets*. Los valores obligatorios son la cadena de conexión (`ConnectionStrings:AspelSae`) y, para `--once`, la sección `Api` (`Api:BaseUrl` y `Api:AgentKey`). Si falta alguno o está mal escrito, el programa termina con el código 1 y dice cuál. `--test` funciona sin la sección `Api`: solo avisa que no la comprobó.

### En desarrollo: User Secrets

`dotnet run` activa el entorno `Development` (lo hace [`Properties/launchSettings.json`](src/SyncAgent/Properties/launchSettings.json)) y con él los secretos. La clave se toma del `.env` para no teclearla:

```powershell
cd sync-agent\src\SyncAgent
$p = ((Get-Content ..\..\..\.env | Where-Object { $_ -like 'CATALOGO_LECTURA_PASSWORD=*' }) -replace '^CATALOGO_LECTURA_PASSWORD=','').Trim()
dotnet user-secrets set "ConnectionStrings:AspelSae" "Server=127.0.0.1,14330;Database=SAE70Empre01;User Id=catalogo_lectura;Password=$p;TrustServerCertificate=True;"
cd ..\..
dotnet run -c Release --project src\SyncAgent -- --test
```

El secreto vive fuera del repositorio (`%APPDATA%\Microsoft\UserSecrets`). Para borrarlo: `dotnet user-secrets clear` dentro de `src\SyncAgent`. El backend **nunca** se conecta a SQL Server; esta cadena es solo del agente.

Para la API, desde la raíz del repositorio:

```powershell
.\sync-agent\scripts\New-SyncAgentKey.ps1 -Development          # Api:BaseUrl = http://localhost:3000
```

El script genera una llave de desarrollo y la guarda en los User Secrets del agente (`Api:AgentKey` y `Api:BaseUrl`). En los del backend guarda el hash (`Sync:AgentKeyHashes`).

> **Advertencia:** cada vez que se ejecuta, `-Development` **reemplaza** `Sync:AgentKeyHashes` en los secretos del backend. Cualquier otra instalación local con una llave distinta (por ejemplo la tarea programada instalada con `-ConfigPath`) recibe `401` hasta que su hash se agregue. Para aceptar dos llaves a la vez (máximo 2): `dotnet user-secrets set "Sync:AgentKeyHashes" "<hash 1>,<hash 2>" --project backend\src\backend`. La salida del script lo recuerda. Con `-BaseUrl http://localhost:5039` el agente habla directo con el backend. Por omisión pasa por el frontend (`npm run dev`), como en Railway. Después hay que reiniciar el backend.

### En la tienda: `appsettings.Production.json`

Un archivo junto al `.exe`, **fuera de git** (está en `.gitignore`). El repositorio solo trae [`appsettings.Production.example.json`](src/SyncAgent/appsettings.Production.example.json), con la contraseña `CAMBIAR`. El instalador lo copia y le restringe los permisos (ver [paso 4](#4-configurar-la-conexión) y [paso 7 de la instalación](#7-configurar-la-llave-del-agente)). Se prefiere un archivo a las variables de entorno de máquina porque estas las puede leer cualquier proceso del servidor, mientras que el archivo solo lo leen Administradores, SYSTEM y la cuenta del agente.

```json
{
  "ConnectionStrings": {
    "AspelSae": "Server=localhost;Database=SAE70Empre01;User Id=catalogo_lectura;Password=<la clave>;TrustServerCertificate=True;"
  },
  "Api": {
    "BaseUrl": "https://<dominio>.up.railway.app",
    "AgentKey": "<la escribe New-SyncAgentKey.ps1>"
  }
}
```

- `Api:BaseUrl` es la URL **pública** del sitio, la misma que abre el cliente. El frontend reenvía `/api/*` al backend.
  - Debe usar `https`; `http` solo se acepta con `localhost` o `127.0.0.1`.
  - Va sin ruta, sin `?` y sin `#`.
- `Api:AgentKey` **no se escribe a mano**: la genera y la escribe `New-SyncAgentKey.ps1 -ConfigPath …` ([paso 7 de la instalación](#7-configurar-la-llave-del-agente)). El ejemplo trae `CAMBIAR`, que el agente interpreta como "sin llave".

### Opciones

| Clave | Valor de fábrica | Qué hace |
|---|---|---|
| `Aspel:PriceList` | `1` | Lista de precios a leer (`CVE_PRECIO`). La regla comercial usa la 1. |
| `Aspel:CommandTimeoutSeconds` | `60` | Tiempo máximo de la consulta. |
| `Aspel:ConnectTimeoutSeconds` | `15` | Tiempo máximo para conectar. |
| `Aspel:Retry:MaxAttempts` | `4` | Intentos totales (1 más 3 reintentos). |
| `Aspel:Retry:DelaysSeconds` | `[5, 15, 45]` | Espera antes de cada reintento. Si el archivo la define, **reemplaza por completo** a la de fábrica. No puede estar vacía ni tener valores negativos: el agente no arranca y dice por qué. |
| `Agent:DataDirectory` | `data` | Carpeta de estado y logs (relativa a la del programa). |
| `Agent:LogRetentionDays` | `14` | Días que se conservan los logs. |
| `Api:BaseUrl` | *(obligatorio para `--once`)* | URL pública del sitio. |
| `Api:AgentKey` | *(obligatorio para `--once`)* | Llave del agente (43 caracteres). |
| `Api:BatchSize` | `1000` | Artículos por lote (entre 100 y 2000). Con 1000, cada lote mide unos 106 KB; el reenvío de Next.js corta a partir de 10 MB (el cliente recibe `500`). |
| `Api:RequestTimeoutSeconds` | `30` | Tiempo máximo de cada solicitud (iniciar, lote, comprobar). |
| `Api:CompleteTimeoutSeconds` | `120` | Tiempo máximo de "completar". Si el proxy corta antes (30 s), el reintento recibe el resumen ya confirmado. |
| `Api:TotalTimeoutMinutes` | `8` | Tiempo máximo de todo el envío, por debajo del límite de 10 min de la tarea. |
| `Api:MaxAttempts` | `4` | Intentos por solicitud ante fallos transitorios, con esperas de 5, 15 y 45 s. |

Solo se reintentan los fallos transitorios. Un inicio de sesión fallido (error 18456) **no** se reintenta: repetirlo solo bloquearía la cuenta. Con la API pasa lo mismo con `400`, `401`, `403`, `404`, `409` y `422`: esperar no los arregla.

## 6. Modos de uso y códigos de salida

```powershell
SyncAgent.exe --test     # una lectura, un resumen y una comprobación de la API; no envía productos ni guarda nada
SyncAgent.exe --once     # una sincronización completa: lee y envía (la que ejecuta la tarea programada)
```

`--test` es lo que se usa en la tienda para validar la consulta contra la base real y, cuando ya hay llave, la conexión con la API.

- Solo escribe en la consola (no crea archivos), de modo que se puede ejecutar a mano como Administrador sin ensuciar `data\`.
- La comprobación de la API es un `GET /api/sync/ping` con la llave: no envía productos ni cambia nada en el catálogo.

Extracto del informe (con la base de prueba de Docker):

```
--- Artículos ---
  Total de filas leídas                                  14
  Códigos distintos                                      14   (igual al total: el JOIN no duplica)
  Activos (STATUS = A)                                   13
--- Precios (solo activos) ---
  Activos sin precio en la lista                          2
--- Códigos (CVE_ART) ---
  Con espacios al inicio                                  1
  Grupos que solo difieren por espacios                   1   (productos distintos; no se recortan)
--- Tiempos ---
  Conexión: 142 ms     Consulta + lectura: 158 ms
--- Memoria del proceso ---
  Máximo en memoria física (working set):     48.7 MB
  Memoria disponible según el GC:             64.0 MB   (con el tope activo debe ser 64)

--- API ---
  URL: https://<dominio>.up.railway.app
  Responde y la llave es válida. (No se envió ningún producto.)
```

Si la API no está configurada, la sección dice `Sin configurar` y el código de salida sigue siendo 0. Si está configurada pero no responde o rechaza la llave, dice `NO SE PUDO COMPROBAR: …` y el código es 5.

**Cómo leerlo en la tienda.** Las cifras cambian con el inventario. La primera ejecución en la tienda (2026-11-24) dio:

- **artículos:** 18 665 (18 615 activos y 50 de baja), con «Códigos distintos» igual al total;
- **precios:** 0 activos sin precio en la lista 1, 120 activos con precio 0 y 2 con precio de 100 000 o más;
- **descripciones:** 10 nulas y 23 vacías (27 activos sin descripción utilizable);
- **códigos:** 18 con espacios al inicio, en 14 grupos que solo difieren por espacios;
- **existencias:** 398 con decimales y 118 de un millón o más.

Lo que importa es:

- que **«Códigos distintos» sea igual al total de filas** (si es menor, la consulta duplica artículos y hay que revisarla);
- que los **activos sin precio o con precio 0 aparezcan** (no se pierden; la API los guarda con precio 0 y no los muestra);
- que haya **pares de códigos con espacios** (se entregan como productos distintos);
- los **tiempos** y la **memoria máxima** reales del servidor.

`--once` guarda `data\sync-state.json` y escribe `data\logs\agent-AAAA-MM-DD.log`. El estado incluye:

- el resultado de la última ejecución, la **última sincronización exitosa** y los fallos seguidos;
- los artículos leídos, la duración y la memoria;
- el id de la última corrida confirmada, la huella del inventario y el resumen de la API (insertados, actualizados, desactivados, rechazados, o `unchanged`).

**Una ejecución solo cuenta como exitosa cuando la API confirmó la corrida.** Un fallo suma el contador pero **no borra** la fecha de la última sincronización exitosa. Si la API rechaza artículos (código vacío o valores fuera de rango), el log los lista con su código para corregirlos en Aspel; la corrida sigue siendo exitosa. Una segunda ejecución simultánea no se solapa: sale de inmediato con el código 3.

| Código | Significado | Qué hacer |
|---|---|---|
| `0` | Correcto | — |
| `1` | Configuración inválida o incompleta | Leer el mensaje: dice cuál valor falta o sobra. |
| `2` | No se pudo leer SQL Server (después de los reintentos) | Ver la tabla de [solución de problemas](#12-solución-de-problemas) por número de error. |
| `3` | Ya hay otra ejecución en curso | Normal si una ejecución tarda más que el intervalo; la tarea la ignora. |
| `4` | Error inesperado | Ver el log de `data\logs\`. |
| `5` | No se pudo enviar a la API (o, en `--test`, comprobarla), después de los reintentos. El catálogo del sitio **no cambió**. | Leer el mensaje: llave rechazada, freno de seguridad, sin conexión… Ver la tabla de [solución de problemas](#12-solución-de-problemas). |

El Programador de tareas muestra este código en *«Último resultado»*.

## 7. Pruebas

Desde `sync-agent\` (con el SDK 10):

```powershell
dotnet test --solution SyncAgent.slnx -c Release
```

Sin base de datos corren las **pruebas unitarias** (202) y el resumen de `dotnet test` muestra `total: 214`, `succeeded: 202` y `skipped: 12` (una prueba con 6 variantes cuenta como una sola cuando se omite). Las **17 pruebas de integración** (`Category=Integration`) usan la base de Docker y **se omiten solas** si no existe la variable `SYNCAGENT_TEST_CONNECTION`:

```powershell
$p = ((Get-Content ..\.env | Where-Object { $_ -like 'CATALOGO_LECTURA_PASSWORD=*' }) -replace '^CATALOGO_LECTURA_PASSWORD=','').Trim()
$env:SYNCAGENT_TEST_CONNECTION = "Server=127.0.0.1,14330;Database=SAE70Empre01;User Id=catalogo_lectura;Password=$p;TrustServerCertificate=True;"
dotnet test --solution SyncAgent.slnx -c Release       # ahora: total: 219, succeeded: 219, skipped: 0
```

> **Nunca apunte `SYNCAGENT_TEST_CONNECTION` a la base real de Aspel.** Algunas pruebas intentan escribir (para comprobar que falla). Por seguridad, antes de cada prueba se verifica que la base sea la de prueba (exactamente los 14 artículos sembrados); si no lo es, todas fallan sin hacer nada, y los intentos de escritura van dentro de una transacción que siempre se revierte.

El CI ejecuta solo las unitarias:

```powershell
dotnet test --solution SyncAgent.slnx --no-build -c Release -- --filter-not-trait "Category=Integration"
```

Se usa Microsoft.Testing.Platform (xunit v3); por eso la opción `--solution` y los argumentos después de `--`.

## 8. Publicar

```powershell
cd sync-agent
dotnet publish src\SyncAgent -c Release -r win-x64 --self-contained -o publish
```

Genera `publish\` (unos 243 archivos, ~89 MB): incluye el runtime de .NET, así que el servidor no necesita instalar nada. Va precompilado (ReadyToRun) para gastar menos CPU en cada arranque y **no** usa recorte (*trimming*), que es arriesgado con SqlClient. `publish\` está en `.gitignore`.

Cifras medidas (referencia, en un portátil con la base de Docker; en la tienda se miden con `--test`):

| | Resultado |
|---|---|
| 18 460 artículos: consulta y lectura | ~90 ms |
| 18 460 artículos: heap administrado | 9.2 MB de los 64 MB permitidos |
| Pico de memoria física del proceso | ~58 MB con 18 460 artículos; ~48 MB con 14 |

## 9. Instalación en la tienda

**Quién**: una persona con permisos de **administrador de Windows** en el servidor y de **sysadmin** en SQL Server. **Cuánto**: unos 15 minutos. **Cuándo**: el día de la instalación (la cuenta `catalogo_lectura` se crea ese día).

### Lista rápida: día de la instalación en el controlador de dominio

Es el recorrido completo para la tienda, en orden. Cada paso remite a su detalle más abajo. En un equipo que **no** es controlador de dominio cambia solo el paso 3 (se crea la cuenta `svc-nettes-sync` y se pide su contraseña).

**Antes de ir** (desde cualquier equipo)

1. **Publicar el agente con el código de `main`.** Un `publish` hecho con una versión anterior no envía nada a la API. Desde `sync-agent\`: `dotnet publish src\SyncAgent -c Release -r win-x64 --self-contained -o publish` ([sección 8](#8-publicar)).
2. **Comprobar Railway:**
   - `https://<dominio>/api/health` responde `Healthy`;
   - `https://<dominio>/api/sync/ping` responde **401** (la ruta existe y pide llave; un `404` significa que sigue desplegado el backend anterior);
   - en los logs del pre-deploy del backend aparece `Migraciones aplicadas.`
3. **Tener a mano** el acceso al panel de Railway y la contraseña de `catalogo_lectura` ([quién la custodia](#10-contraseñas) está pendiente de confirmar).

**En el servidor**, en Windows PowerShell **como administrador** (no PowerShell 7)

| # | Qué hacer | Qué debe pasar |
|---|---|---|
| 1 | Copiar `publish\`, `scripts\` y `sql\` juntas a `C:\Temp\NettesSyncAgent\` y ejecutar `Get-ChildItem C:\Temp\NettesSyncAgent -Recurse \| Unblock-File` ([paso 0](#0-preparar)) | Sin errores |
| 2 | `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force` y `Set-Location C:\Temp\NettesSyncAgent\scripts` | Estar en la carpeta `scripts` |
| 3 | `.\Install-SyncAgent.ps1 -SkipTestPrompt` ([paso 3](#3-instalar-la-tarea-programada)). La cuenta `catalogo_lectura` ya existe en la tienda; si no, primero el [paso 2](#2-crear-la-cuenta-de-sql-server) | Avisa `CONTROLADOR DE DOMINIO`; `No hay ninguna cuenta 'svc-nettes-sync' en el dominio`; `SID propio de la tarea: S-1-5-87-…`; `La tarea quedó DESHABILITADA`. **No pide contraseña.** Después, `Get-ADUser -Filter "Name -eq 'svc-nettes-sync'"` no devuelve nada |
| 4 | `notepad C:\NettesSyncAgent\appsettings.Production.json`: contraseña real de `catalogo_lectura` en `ConnectionStrings:AspelSae` y la URL pública del sitio (`https://…`) en `Api:BaseUrl` ([paso 4](#4-configurar-la-conexión)) | JSON válido |
| 5 | `& 'C:\NettesSyncAgent\SyncAgent.exe' --test` ([paso 5](#5-probar-la-lectura)) | ~18 665 filas; «Códigos distintos» igual al total. La sección `--- API ---` todavía no puede validar la llave |
| 6 | `.\New-SyncAgentKey.ps1 -ConfigPath C:\NettesSyncAgent\appsettings.Production.json` ([paso 7](#7-configurar-la-llave-del-agente)) | Muestra **solo el hash** (la llave no se ve). Cópielo |
| 7 | En Railway, servicio `backend`, variable `Sync__AgentKeyHashes` = ese hash. Espere el redespliegue | El backend vuelve a estar `Healthy` |
| 8 | Otra vez `SyncAgent.exe --test` | `--- API ---`: «Responde y la llave es válida». No envía productos |
| 9 | `Enable-ScheduledTask -TaskPath '\NettesSyncAgent\' -TaskName 'Sincronizacion'` y `Start-ScheduledTask -TaskPath '\NettesSyncAgent\' -TaskName 'Sincronizacion'`; espere 1 o 2 minutos ([paso 8](#8-habilitar-la-tarea-ejecutarla-una-vez-y-verificar)) | La tarea termina |
| 10 | Verificar la tarea: `Get-ScheduledTask -TaskPath '\NettesSyncAgent\' \| Get-ScheduledTaskInfo`; `Get-Content C:\NettesSyncAgent\data\sync-state.json -Encoding UTF8`; el último log; `icacls C:\NettesSyncAgent` | `LastTaskResult : 0`; `"lastResult": "succeeded"` con `lastPublish`; una línea `API: corrida … aplicada` (la primera vez, ~18 665 leídos y casi todos insertados); permisos solo para Administradores, SYSTEM y `NT TASK\NettesSyncAgent-Sincronizacion` |

**En el sitio**

11. `https://<dominio>/api/products?pageSize=1`: el `totalCount` debe quedar cerca de los activos con precio mayor que 0 y con nombre. Con las cifras de la primera lectura (18 615 activos, 120 con precio 0, 27 sin descripción utilizable y 2 con precio de 100 000 o más) son unos 18 460. Es normal que sea **menor** que el total de Aspel: [qué ve el cliente](../docs/architecture.md#52-qué-productos-ve-el-cliente). Revise también el catálogo en el navegador.
12. **Reinicie el servidor** (todavía sin verificar en la tienda) y compruebe que la tarea corre sola: `LastRunTime` avanza cada 15 minutos y `lastSuccessAt` se actualiza.

**Si algo falla:** `Disable-ScheduledTask -TaskPath '\NettesSyncAgent\' -TaskName 'Sincronizacion'` la detiene. El log está en `C:\NettesSyncAgent\data\logs\` y las causas frecuentes en la [tabla de problemas](#12-solución-de-problemas) (llave rechazada, freno de seguridad, sin conexión). `Uninstall-SyncAgent.ps1` deshace todo sin tocar cuentas.

### 0. Preparar

Copie al servidor las carpetas `publish\`, `scripts\` y `sql\` **conservando la estructura** (los scripts buscan `..\publish` y `..\sql` junto a ellos), por ejemplo en `C:\Temp\NettesSyncAgent\`. Si llegaron descargadas o por correo, desbloquéelas:

```powershell
Get-ChildItem C:\Temp\NettesSyncAgent -Recurse | Unblock-File
```

Abra **Windows PowerShell como administrador** (clic derecho → *Ejecutar como administrador*; no PowerShell 7). Esa ventana abre en `C:\Windows\System32`: **entre a la carpeta de los scripts** antes de ejecutar nada, o las rutas relativas no se encontrarán.

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force     # solo vale para esta ventana
Set-Location C:\Temp\NettesSyncAgent\scripts
```

### 1. Inspeccionar la base de Aspel (solo lectura)

Confirma los tipos reales de las columnas y la forma de los datos. **No modifican nada** y no necesitan contraseñas (usan la autenticación de Windows):

```powershell
sqlcmd -S localhost -E -b -i ..\sql\install\02-inspeccionar-columnas.sql -o C:\Temp\salida-02.txt
sqlcmd -S localhost -E -b -i ..\sql\install\03-configuracion-memoria.sql -o C:\Temp\salida-03.txt
```

Guarde y envíe ambos archivos al equipo: `02` confirma si `CVE_ART` es `char` o `varchar` y su largo, y si hay artículos duplicados por el `JOIN`; `03` muestra `max server memory` (solo lo muestra, no lo cambia).

### 2. Crear la cuenta de SQL Server

```powershell
.\Set-CatalogoLecturaLogin.ps1
```

Pide la clave nueva dos veces (ver [Contraseñas](#10-contraseñas)), crea `catalogo_lectura` con **`SELECT` solo sobre `dbo.INVE01` y `dbo.PRECIO_X_PROD01`**, y al final comprueba que puede iniciar sesión y leer. Si su cuenta de Windows no es administradora de SQL Server, use `-SqlAdminUser sa`.

### 3. Instalar la tarea programada

```powershell
.\Install-SyncAgent.ps1
```

En un equipo normal pide la contraseña de la cuenta de Windows `svc-nettes-sync` (dos veces); **en un controlador de dominio no pide nada** (ver más abajo). Hace, mostrando cada paso:

1. Verifica que es administrador y que existe la carpeta publicada.
2. Crea la **cuenta local dedicada**, sin privilegios de administrador, y le concede **un único derecho especial**: «Iniciar sesión como trabajo por lotes». Windows **no** lo concede solo; sin él la tarea nunca arranca (ver [solución de problemas](#12-solución-de-problemas)). **En un controlador de dominio no crea ninguna cuenta y usa LOCAL SERVICE** (ver abajo).
3. Copia el programa a `C:\NettesSyncAgent` sin tocar la configuración ni los datos que ya existan.
4. Aplica permisos NTFS de mínimo privilegio (tabla siguiente).
5. Crea `appsettings.Production.json` a partir del ejemplo y lo protege.
6. Registra la tarea programada.
7. Muestra un resumen y los comandos para verificar.

**Si la configuración está incompleta, la tarea se registra DESHABILITADA.** Si falta la contraseña real de `catalogo_lectura`, `Api:BaseUrl` o `Api:AgentKey`, el instalador no deja la tarea activa: así no corre ni deja un estado de error antes de tiempo. El resumen final lista lo que falta y muestra el comando exacto para habilitarla (`Enable-ScheduledTask`, paso 8). Si la configuración ya está completa (por ejemplo, al actualizar), la tarea queda habilitada. Además, el agente rechaza con el código 1, sin abrir la conexión, una cadena de conexión con la contraseña de ejemplo (`CAMBIAR`).

Se puede ejecutar varias veces: lo que ya existe se **actualiza**. `Get-Help .\Install-SyncAgent.ps1 -Full` muestra todas las opciones (`-InstallPath`, `-AccountName`, `-IntervalMinutes`, `-UseLocalService`…).

**En un controlador de dominio (el servidor de la tienda lo es)**

Un controlador de dominio **no tiene cuentas locales**: `New-LocalUser` crea la cuenta **en el dominio** y el grupo local «Usuarios» (`S-1-5-32-545`) no existe. Por eso el instalador revisa el rol del equipo (`Win32_ComputerSystem.DomainRole` 4 o 5) y, si es controlador de dominio:

- **no crea ninguna cuenta** ni pide contraseña. Antes de cualquier `New-LocalUser`, `Set-LocalUser`, `Add-LocalGroupMember` o `Remove-LocalUser`, los scripts vuelven a comprobar el rol y abortan si es un controlador de dominio;
- registra la tarea con la cuenta integrada **LOCAL SERVICE** (`S-1-5-19`, `LogonType ServiceAccount`, sin el derecho de inicio de sesión por lotes) y con `ProcessTokenSidType = Unrestricted`: el Programador de tareas agrega al proceso un **SID propio de la tarea** (`NT TASK\NettesSyncAgent-Sincronizacion`, `S-1-5-87-…`);
- da los permisos NTFS a **ese SID** y no a LOCAL SERVICE. El SID se obtiene con `schtasks /showsid /tn "\NettesSyncAgent\Sincronizacion"` después de registrar la tarea y es estable mientras la tarea conserve la ruta:
  - lectura y ejecución en el programa;
  - modificación solo en `data\`;
  - solo lectura en `appsettings.Production.json`, sin herencia: Administradores, SYSTEM y el SID de la tarea;
  - Administradores y SYSTEM siguen con control total en todo, y LOCAL SERVICE no tiene ningún permiso.

En un equipo que no es controlador de dominio nada cambia: se crea `svc-nettes-sync` (`ProcessTokenSidType` solo aplica a LOCAL SERVICE y NETWORK SERVICE). Para probar el camino de LOCAL SERVICE en otro equipo: `.\Install-SyncAgent.ps1 -UseLocalService`. `Uninstall-SyncAgent.ps1` detecta el modo solo, por el rol del equipo, por la cuenta de la tarea registrada o por permisos de LOCAL SERVICE o de un SID de tarea (`S-1-5-87-…`) en la carpeta. En modo LOCAL SERVICE no busca ni elimina ninguna cuenta.

**Por qué el SID propio de la tarea.** LOCAL SERVICE la comparten varios servicios de Windows. Si el permiso fuera para LOCAL SERVICE, cualquiera de ellos podría leer `appsettings.Production.json`, que tiene la contraseña de `catalogo_lectura` **y la llave del agente**. Con el SID propio, solo el proceso de esta tarea lleva ese SID en su token: **otros servicios con LOCAL SERVICE no pueden leer la configuración** (se comprueba con una tarea temporal; ver los comandos de prueba).

- Si la tarea **cambia de ruta o de nombre**, su SID cambia y los permisos dejan de valer: hay que volver a ejecutar `Install-SyncAgent.ps1`.
- **No se limitan los privilegios** de la tarea (`RequiredPrivileges`). El agente no pide ninguno especial, pero no se pudo verificar en un controlador de dominio cuáles necesitan .NET, SqlClient y las conexiones TLS; quitar uno equivocado haría fallar la tarea sin un mensaje claro. LOCAL SERVICE ya trae pocos privilegios.

*Opcional:* una cuenta de servicio administrada de grupo (**gMSA**) daría al agente una identidad propia en el dominio, sin contraseña que guardar. No hace falta para proteger la configuración.

**Red:** LOCAL SERVICE sale a la red como **anónimo** (sin las credenciales del equipo). No afecta al agente: se conecta a SQL Server en el mismo servidor con usuario de SQL (`catalogo_lectura`) y a Railway por HTTPS sin autenticación de Windows. Solo sería un problema si la red usara un proxy que pide usuario de Windows; en la tienda la salida HTTPS a Railway funciona directa.

**Permisos NTFS**

| | `C:\NettesSyncAgent` | `data\` | `appsettings.Production.json` |
|---|---|---|---|
| Administradores y SYSTEM | Control total | Control total | Control total |
| Cuenta de la tarea (`svc-nettes-sync` o, en un controlador de dominio, el SID propio de la tarea) | Lectura y ejecución | **Modificar** (única carpeta donde escribe) | Solo lectura |
| Los demás usuarios | Sin acceso | Sin acceso | Sin acceso |

**Opciones de la tarea** (`\NettesSyncAgent\Sincronizacion`), cada una corresponde a una casilla del Programador de tareas:

| Opción | Valor | Para qué |
|---|---|---|
| Ejecutar aunque no haya sesión iniciada | `LogonType: Password` | El servidor corre sin que nadie esté conectado. |
| Cuenta dedicada, no administradora | `RunLevel: LeastPrivilege` | Si el agente fallara, el daño se limita a lo que esa cuenta puede hacer. |
| Primer disparo al encender el servidor | `BootTrigger`, retraso de 3 min | Da tiempo a que arranquen SQL Server y Aspel. |
| Luego cada 15 minutos, sin fin | `TimeTrigger` con repetición y sin duración | Sincronización periódica. |
| No iniciar otra si la anterior sigue | `MultipleInstancesPolicy: IgnoreNew` | Evita ejecuciones solapadas. |
| Tiempo máximo de una ejecución | `ExecutionTimeLimit`: 10 min | Si se cuelga, Windows la termina. |
| Si el servidor estaba apagado a la hora prevista | `StartWhenAvailable` | Se ejecuta en cuanto sea posible. |
| Prioridad baja | `Priority: 7` | Cede CPU a Aspel y a SQL Server. |
| Reintento si la tarea falla | `RestartOnFailure`: 3 veces, cada 1 min | Red de seguridad. No se ha verificado que reaccione a un código de salida distinto de 0; los fallos de SQL ya los cubren los reintentos internos y la siguiente ejecución a los 15 minutos. |

### 4. Configurar la conexión

Edite el archivo **como administrador** (la cuenta del agente solo puede leerlo):

```powershell
notepad C:\NettesSyncAgent\appsettings.Production.json
```

Reemplace `CAMBIAR` por la clave de `catalogo_lectura`. Como esa clave solo lleva letras, números y `- _ . ! * + @`, no hace falta escapar nada ni usar comillas dentro de ella. El archivo siempre trae las secciones `ConnectionStrings` y `Api`: el instalador agrega las que falten.

### 5. Probar la lectura

```powershell
& 'C:\NettesSyncAgent\SyncAgent.exe' --test
```

Compare con [cómo leer el informe](#6-modos-de-uso-y-códigos-de-salida). Es el momento de confirmar que «Códigos distintos» es igual al total de filas.

### 6. Verificar los permisos de la cuenta de SQL

```powershell
sqlcmd -S localhost -U catalogo_lectura -d SAE70Empre01 -b -i ..\sql\install\04-verificar-permisos.sql
```

Como no se indica `-P`, `sqlcmd` pide la contraseña en una línea oculta. Debe terminar con *«catalogo_lectura solo tiene SELECT sobre dbo.INVE01 y dbo.PRECIO_X_PROD01»*. **No escribe nada** en Aspel: solo pregunta a SQL Server qué permisos tiene la cuenta.

### 7. Configurar la llave del agente

La llave identifica al agente ante la API. Se genera **en este servidor** y nunca sale de él: a Railway solo va su hash.

1. Edite `C:\NettesSyncAgent\appsettings.Production.json` como administrador y ponga en `Api:BaseUrl` la URL pública del sitio, por ejemplo `https://<dominio>.up.railway.app`.
2. Genere la llave (en la misma consola de administrador, desde la carpeta `scripts`):

   ```powershell
   .\New-SyncAgentKey.ps1 -ConfigPath C:\NettesSyncAgent\appsettings.Production.json
   ```

   El script escribe la llave en `Api:AgentKey` (el archivo conserva sus permisos) y muestra **solo el hash**. La llave no se ve en pantalla y no pasa por la línea de comandos, así que no queda en el historial de PowerShell.
3. En Railway, servicio `backend`, *Variables*: `Sync__AgentKeyHashes` = el hash mostrado ([docs/deployment.md](../docs/deployment.md#3-variables-nombres-sin-valores)). Espere a que el backend se redespliegue.
4. Compruebe:

   ```powershell
   & 'C:\NettesSyncAgent\SyncAgent.exe' --test
   ```

   La sección `--- API ---` debe decir *«Responde y la llave es válida»*. Esto **no** envía productos.

**Rotar la llave** (si se filtró o por rutina). El script escribe la llave nueva en el archivo **antes** de que Railway conozca su hash; para que el agente no falle en medio, se deshabilita la tarea:

1. `Disable-ScheduledTask -TaskPath '\NettesSyncAgent\' -TaskName 'Sincronizacion'`
2. Ejecute otra vez el paso 2, que genera una llave nueva y muestra su hash.
3. En Railway ponga `Sync__AgentKeyHashes` = `<hash anterior>,<hash nuevo>` (sirven las dos) y espere a que el backend se redespliegue.
4. `SyncAgent.exe --test` debe decir *«la llave es válida»*; entonces `Enable-ScheduledTask -TaskPath '\NettesSyncAgent\' -TaskName 'Sincronizacion'`.
5. Cuando una sincronización termine bien, deje solo el hash nuevo en Railway.

Para saber qué hash corresponde a una llave existente: `.\New-SyncAgentKey.ps1 -HashFromPrompt`.

### 8. Habilitar la tarea, ejecutarla una vez y verificar

Si el instalador la dejó deshabilitada (configuración incompleta), habilítela ahora que la configuración y la llave están completas. Una tarea deshabilitada no se puede iniciar a mano.

```powershell
Enable-ScheduledTask -TaskPath '\NettesSyncAgent\' -TaskName 'Sincronizacion'
Start-ScheduledTask -TaskPath '\NettesSyncAgent\' -TaskName 'Sincronizacion'
Start-Sleep -Seconds 60
Get-ScheduledTask -TaskPath '\NettesSyncAgent\' | Get-ScheduledTaskInfo | Format-List LastRunTime,LastTaskResult,NextRunTime
Get-Content C:\NettesSyncAgent\data\sync-state.json -Encoding UTF8
Get-Content (Get-ChildItem C:\NettesSyncAgent\data\logs\*.log | Select-Object -Last 1) -Tail 12
```

Debe mostrar:

- `LastTaskResult : 0` y `"lastResult": "succeeded"`;
- los artículos leídos;
- en `lastPublish`, el resumen de la API;
- en el log, una línea *«API: corrida … aplicada. N leídos, N insertados…»*. La primera vez son unos 18 665 insertados (la cifra cambia con el inventario); las siguientes, `unchanged` si nada cambió.

A partir de ahí la tarea corre sola cada 15 minutos y después de cada reinicio. Para ver permisos: `icacls C:\NettesSyncAgent` y `icacls C:\NettesSyncAgent\data`.

> **Primera carga:** si el log dice *«Freno de seguridad»*, la API detuvo una corrida que desactivaría muchos productos (por ejemplo, si el catálogo de Railway tiene productos de prueba que no existen en Aspel). No cambió nada. Revise que el agente lea la base correcta; si la baja es real, siga [la tabla de problemas](#12-solución-de-problemas).

## 10. Contraseñas

Hay **dos** contraseñas y una **llave**, y no se confunden.

| | Para qué sirve | Dónde vive | Quién la guarda |
|---|---|---|---|
| `catalogo_lectura` (SQL Server) | El agente se conecta con ella a la base de Aspel. | Solo en `appsettings.Production.json` del servidor, con permisos restringidos. | La persona responsable del servidor de la tienda, en su gestor de contraseñas. |
| `svc-nettes-sync` (cuenta de Windows) | La tarea inicia sesión con ella sin que nadie esté conectado. **No existe en un controlador de dominio** (la tienda): ahí la tarea usa LOCAL SERVICE, que no tiene contraseña. | Solo la conocen Windows y el Programador de tareas; no se escribe en ningún archivo. | La misma persona. Nadie la necesita en el día a día. |
| Llave del agente (`Api:AgentKey`) | El agente se identifica ante la API. | Solo en `appsettings.Production.json`. En Railway está **su hash**, no la llave. | Nadie necesita conocerla: la genera `New-SyncAgentKey.ps1` y no se muestra. Si se pierde o se filtra, se genera otra ([paso 7](#7-configurar-la-llave-del-agente)). |

- **Nunca** van en el repositorio, en este README, en el chat ni en capturas de pantalla. `appsettings.Production.json` está en `.gitignore`; el repositorio solo trae el ejemplo con `CAMBIAR`.
- **Quién las guarda** lo designa el negocio para el servidor de la tienda (**pendiente de confirmar**). El equipo de desarrollo no necesita conocerlas.

**Qué caracteres puede tener cada una**

- **`svc-nettes-sync` (Windows): cualquiera.** Solo debe cumplir la directiva de contraseñas del servidor. Nunca se pega como texto dentro de otro archivo: viaja protegida hasta Windows.
- **`catalogo_lectura` (SQL Server): solo letras (A–Z, a–z), números (0–9) y estos símbolos: `-` `_` `.` `!` `*` `+` `@`.** Mínimo 12 caracteres, con mayúscula, minúscula, número y símbolo. **No se permiten:** comilla simple `'`, comilla doble `"`, punto y coma `;`, signo de dólar `$`, barra invertida `\` ni espacios.
- **Por qué esa restricción:** la clave de SQL termina escrita como texto en dos lugares y cada uno trata esos caracteres como instrucciones, no como parte de la clave. (1) La cadena de conexión de `appsettings.Production.json`: allí `;` separa las opciones y las comillas agrupan valores, y como el archivo es JSON, `"` y `\` también son especiales. (2) El script SQL y `sqlcmd`: una `'` cierra el texto de la instrucción y `$(` se interpreta como una variable. Con letras, números y esos siete símbolos no hay ambigüedad en ninguno. El script que pide la clave **la valida** y la rechaza con un mensaje que dice qué carácter sobra.

**Cómo se escriben cuando los scripts las piden**

`Install-SyncAgent.ps1` y `Set-CatalogoLecturaLogin.ps1` las piden con `Read-Host -AsSecureString`. Se teclea la clave en la ventana (no se ve lo que escribe, o salen asteriscos), **sin comillas**, y se pulsa Enter; hay que escribirla **dos veces** y deben coincidir. Como no es un comando, **no entra al historial de PowerShell** (`ConsoleHost_history.txt`). `Set-CatalogoLecturaLogin.ps1` además no lanza ningún programa: usa el cliente SQL que trae PowerShell, así que la clave tampoco aparece en ninguna línea de comandos ni en la lista de procesos. Por eso **no se recomienda** `sqlcmd -v LoginPassword=…`: esa forma sí la deja en el historial y a la vista de los procesos mientras corre.

**Si se pierde o hay que cambiar una**

Las contraseñas **no se recuperan, se reemplazan**.

- **`catalogo_lectura`:** con una cuenta administradora de SQL Server, ejecute otra vez `.\Set-CatalogoLecturaLogin.ps1` (si la cuenta existe, solo le cambia la clave; no hace falta la anterior). Luego edite `appsettings.Production.json` como administrador y compruebe con `SyncAgent.exe --test` y con `04-verificar-permisos.sql`. **No hay que volver a registrar la tarea**: la configuración se lee en cada ejecución.
- **`svc-nettes-sync`:** ejecute otra vez `.\Install-SyncAgent.ps1` con una clave nueva. Fija la clave en la cuenta y **vuelve a registrar la tarea** con ella, sin tocar la configuración ni los datos. Hágalo también si alguien cambió la clave de esa cuenta por otro medio (por ejemplo, con `net user`): la tarea dejaría de arrancar.

**Si escribió una clave dentro de un comando por error** (por ejemplo `-v LoginPassword=…` o `-AccountPassword (ConvertTo-SecureString '…' -AsPlainText -Force)`): trátela como filtrada y cámbiela con el procedimiento anterior. Luego borre esa línea del historial y cierre esa ventana de PowerShell (el historial de la sesión abierta se guarda al cerrar):

```powershell
$h = (Get-PSReadLineOption).HistorySavePath
(Get-Content $h) | Where-Object { $_ -notmatch 'LoginPassword|AsPlainText|-AccountPassword' } | Set-Content $h -Encoding UTF8
```

No use `Start-Transcript` mientras escribe claves en la línea de comandos: el registro copia lo que se teclea.

## 11. Desinstalación

```powershell
.\Uninstall-SyncAgent.ps1                 # quita todo
.\Uninstall-SyncAgent.ps1 -KeepData       # conserva appsettings.Production.json y data\
```

Detiene y elimina la tarea y su carpeta del Programador, cierra los procesos de la cuenta, le quita el derecho de inicio de sesión por lotes, elimina la cuenta y su perfil, y borra la carpeta de instalación. Al final **comprueba** que no quedó nada y lo informa. Se puede ejecutar varias veces: lo que ya no existe se informa como «ya no existía».

Si la tarea corría como **LOCAL SERVICE** (controlador de dominio o `-UseLocalService`), lo detecta solo y **no busca ni elimina ninguna cuenta**, ni cierra procesos de LOCAL SERVICE, porque son servicios de Windows. Con `-KeepData` quita los permisos de LOCAL SERVICE y del SID de la tarea de lo que conserva. En un controlador de dominio nunca elimina cuentas, ni siquiera con `-Force`; si encuentra en el dominio una cuenta `svc-nettes-sync` (de una versión anterior del instalador), **solo avisa** y muestra los comandos para revisarla y borrarla a mano. El instalador hace el mismo aviso.

- **No toca SQL Server.** La cuenta `catalogo_lectura` se quita con el bloque comentado al final de [`sql/install/01-crear-login-lectura.sql`](sql/install/01-crear-login-lectura.sql).
- **Perfil de usuario:** Windows puede seguir marcando como «cargado» el perfil `C:\Users\svc-nettes-sync` aunque ya nadie lo use. El script espera hasta 60 s y, si no se libera, lo informa como aviso (no es un error). En las pruebas se eliminó **después de reiniciar el equipo y volver a ejecutar el mismo script**.

## 12. Solución de problemas

| Síntoma | Causa probable | Qué hacer |
|---|---|---|
| La tarea nunca arranca: `LastTaskResult : 267011` (*«aún no se ha ejecutado»*) y `LastRunTime` de 1999 | La cuenta no tiene el derecho «Iniciar sesión como trabajo por lotes». En el registro de **Seguridad** hay eventos **4625**, *Logon Type 4*, estado `0xC000015B`. | Ejecute otra vez `Install-SyncAgent.ps1`: concede el derecho. Para ver el evento: `Get-WinEvent -FilterHashtable @{LogName='Security';Id=4625} -MaxEvents 3`. |
| La tarea dejó de arrancar después de cambiar o perder la clave de `svc-nettes-sync` | La tarea guarda la clave anterior. | Ejecute otra vez `Install-SyncAgent.ps1` con una clave nueva. Revise los eventos 4625 del registro de Seguridad. |
| Código `1` | Configuración inválida: falta la cadena de conexión, `CAMBIAR` sin reemplazar, esperas vacías o negativas… | El mensaje dice cuál. Edite `appsettings.Production.json`. |
| Código `2`, error **18456** | Usuario o clave de `catalogo_lectura` incorrectos, o la cuenta está deshabilitada. No se reintenta. | Ejecute `Set-CatalogoLecturaLogin.ps1` y actualice el archivo. |
| Código `2`, error **4060** | La cuenta no puede abrir la base `SAE70Empre01`. | Revise el nombre de la base y ejecute `01-crear-login-lectura.sql` (vía `Set-CatalogoLecturaLogin.ps1`). |
| Código `2`, error **229** | Falta el permiso `SELECT` sobre una de las dos tablas. | Ejecute `Set-CatalogoLecturaLogin.ps1` y compruebe con `04-verificar-permisos.sql`. |
| Código `2`, errores **207** o **208** | La estructura de la tabla no coincide (columna o tabla inexistente): ¿base equivocada? | Compare con la salida de `02-inspeccionar-columnas.sql`. |
| Código `2`, errores **258**, **53**, **10061** | SQL Server apagado, nombre o instancia incorrectos, o TCP/IP deshabilitado. Se reintenta 3 veces. | Revise que SQL Server esté encendido y el valor de `Server=` en la cadena. |
| Código `3` | Otra ejecución sigue en curso. | Normal; no requiere acción. |
| Código `1`, *«Falta Api:BaseUrl»* o *«Falta Api:AgentKey»* | `--once` necesita la sección `Api`. | [Paso 7 de la instalación](#7-configurar-la-llave-del-agente). |
| Código `5`, *«La API rechazó la llave del agente (401)»* | La llave del archivo no corresponde al hash de Railway, o `Sync__AgentKeyHashes` falta o está mal copiada. | Compare `.\New-SyncAgentKey.ps1 -HashFromPrompt` con la variable de Railway, o genere una llave nueva ([paso 7](#7-configurar-la-llave-del-agente)). |
| Código `5`, *«Freno de seguridad: se desactivarían N de M productos activos»* | La lectura trajo muchos menos artículos de los que hay activos en el sitio: base o empresa equivocada, o lectura incompleta. **No se cambió nada.** | Revise `--test` («Total de filas leídas»). Si de verdad se eliminaron muchos artículos de Aspel, suba `Sync__MaxDeactivationRatio` en Railway para una corrida y luego quite la variable. |
| Código `5`, *«Hay otra sincronización en curso»* | Una corrida anterior no terminó (por ejemplo, se cortó la luz). | Ninguna: a los 15 minutos la API la da por abandonada y la siguiente ejecución sigue. |
| Código `5`, *«No se pudo conectar con la API»* o *«no respondió»* | Sin Internet, DNS o el sitio caído. Se reintenta 3 veces. | Pruebe el sitio en un navegador del servidor y `Test-NetConnection <dominio> -Port 443`. |
| Código `5`, *«respondió con una redirección»* | `Api:BaseUrl` con `http://` o con otro dominio que redirige. | Use exactamente `https://<dominio>` del sitio. |
| Código `5`, *«no encontró la ruta» (404)* | `Api:BaseUrl` apunta a otro sitio o incluye una ruta. | Solo el dominio, sin `/api`. |
| `Install-SyncAgent.ps1` falla en el paso 2 con *«No se encontró el grupo S-1-5-32-545»* (o *«Group … was not found»*) | Versión anterior del instalador en un **controlador de dominio**: ahí no hay grupos locales. Antes de fallar, `New-LocalUser` pudo crear `svc-nettes-sync` **en el dominio**. | Use el instalador actual (detecta el controlador de dominio, usa LOCAL SERVICE y avisa si encuentra la cuenta). Para revisar y borrar la cuenta creada por error, en una consola de administrador del dominio, en este orden: **1.** `Get-ADUser svc-nettes-sync -Properties whenCreated,Description,MemberOf`: la descripción debe ser *«NettesSyncAgent: cuenta del agente»*, la fecha la del intento y `MemberOf` normalmente solo «Usuarios del dominio». **2.** Compruebe si le quedó el derecho de inicio de sesión por lotes: `secedit /export /areas USER_RIGHTS /cfg C:\Temp\derechos.inf` y `Select-String C:\Temp\derechos.inf -Pattern SeBatchLogonRight`, buscando el SID de la cuenta (`(Get-ADUser svc-nettes-sync).SID.Value`); si aparece, quítelo en la directiva *Default Domain Controllers Policy* (*Iniciar sesión como trabajo por lotes*). El instalador anterior falló antes de concederlo, así que lo normal es que no esté. **3.** `Remove-ADUser svc-nettes-sync -Confirm`. No use `Remove-LocalUser` ni un `Uninstall-SyncAgent.ps1` de una versión anterior para esto. |
| La tarea corre como LOCAL SERVICE y no puede escribir en `data\` | Permisos cambiados a mano, o la tarea se registró con otra ruta (su SID cambia). | Ejecute otra vez `Install-SyncAgent.ps1` (repone los permisos) y compruebe con `icacls C:\NettesSyncAgent\data`: debe aparecer `NT TASK\NettesSyncAgent-Sincronizacion:(OI)(CI)(M)` (nunca `LOCAL SERVICE`). |
| *«The term '.\scripts\…' is not recognized»* | La ventana de PowerShell abrió en `C:\Windows\System32`. | `Set-Location` a la carpeta de los scripts. |
| *«running scripts is disabled on this system»* | Política de ejecución de scripts. | `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force` (solo vale para esa ventana). |
| *«A compatible .NET SDK was not found»* al compilar | No está instalado el SDK 10. | `winget install --id Microsoft.DotNet.SDK.10 -e` |
| El perfil `C:\Users\svc-nettes-sync` sigue ahí después de desinstalar | Windows lo mantiene cargado. | Reinicie el equipo y vuelva a ejecutar `Uninstall-SyncAgent.ps1`. |

**Dónde mirar:** `C:\NettesSyncAgent\data\logs\agent-AAAA-MM-DD.log` (uno por día, 14 días) y `C:\NettesSyncAgent\data\sync-state.json`. Los mensajes de error de SQL y de la API se traducen a una explicación en español; el usuario, la contraseña y la llave del agente nunca se escriben.

## 13. Seguridad y criterios de aceptación

| Criterio de US-05 | Estado | Evidencia |
|---|---|---|
| El agente recupera productos, precios y estado correctamente | Cumplido contra la base de prueba; falta la base real | 17 pruebas de integración y `--test` sobre los 14 casos; 18 460 artículos en una carga temporal |
| Los productos sin precio con `CVE_PRECIO = 1` no se pierden | Cumplido (prueba) | `LEFT JOIN`; con `INNER JOIN` fallan 6 pruebas; llegan con precio vacío |
| El agente no modifica datos de Aspel | Cumplido (prueba) | Solo hay un `SELECT` (una prueba lo exige); `INSERT`/`UPDATE`/`DELETE` con `catalogo_lectura` fallan con el error 229 en las dos tablas; la cuenta no pertenece a ningún rol |
| Las credenciales no se exponen en el código ni en los logs | Cumplido (prueba) | Excepciones de SqlClient reales sin la clave; el mensaje de login fallido sale como `Login failed for user '***'`; búsqueda de la clave en `data\`, logs y salida: sin coincidencias |
| No se abre ningún puerto de SQL Server hacia Internet | Cumplido por diseño; falta comprobarlo en la tienda | El agente usa `localhost` y solo hace conexiones salientes HTTPS a la API; el instalador no crea reglas de firewall |

| Criterio de US-06 (parte del agente) | Estado | Evidencia |
|---|---|---|
| `ICatalogPublisher` por HTTP; URL y llave en la sección `Api` de `appsettings.Production.json` | Cumplido (prueba) | `HttpCatalogPublisher` con un `HttpMessageHandler` falso: iniciar, lotes de 1000 y completar, con la llave en cada solicitud |
| La llave no aparece en logs ni en el estado | Cumplido (prueba) | Validación sin repetir la llave; el redactor la oculta; una respuesta hostil que repite la llave no llega al mensaje ni al log |
| `--test` no envía nada; `--once` envía | Cumplido (prueba) | `--test` solo hace `GET /api/sync/ping`; pruebas con ping correcto, 401 y sin red |
| La importación puede repetirse sin duplicar; sin la llave, `401` y nada cambia; un error a mitad no deja el catálogo a medias | Cumplido en la API (19 pruebas de integración contra PostgreSQL, ver `backend/`) | `runId` del agente, lotes reemplazables y completar idempotente; la transacción no depende de la conexión |
| Los datos coinciden con los de Aspel (base de prueba de Docker) | Pendiente | Prueba de punta a punta local (sección 15) |

## 14. Compatibilidad y riesgos

- **.NET 10 y Windows Server 2016.** La [matriz oficial de sistemas soportados de .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) incluye Windows Server 2016 x64.
- **Riesgo del sistema operativo.** El soporte de Windows Server 2016 termina el **12 de enero de 2027** ([ciclo de vida de Microsoft](https://learn.microsoft.com/lifecycle/products/windows-server-2016)). La misma matriz aclara que las versiones sin soporte del fabricante del sistema no se prueban ni se soportan en .NET. El agente seguirá funcionando después de esa fecha, pero sin garantía oficial; el riesgo es del sistema operativo de la tienda, no del agente.
- **Espacio en disco.** El programa ocupa ~89 MB y los logs se conservan 14 días como máximo.
- **Servidor de la tienda (2026-11-24).**
  - Windows Server 2016, **controlador de dominio**, con 4 GB de RAM.
  - SQL Server 2017 en modo mixto, base `SAE70Empre01` con colación `Latin1_General_BIN`. Esa colación es binaria: distingue mayúsculas, minúsculas y acentos, y deja como distintos los códigos que difieren por espacios al inicio, igual que el agente y la API.
- **Memoria de SQL Server.** `max server memory` está **sin límite**. Con 4 GB compartidos, se recomienda al negocio fijar un tope que deje memoria a Windows, a Aspel y al agente. Es una recomendación: el proyecto no cambia la configuración de SQL Server.

## 15. Qué se probó y qué falta verificar en la tienda

**Probado** (portátil con Windows 11, SQL Server 2022 en Docker, SDK 10.0.403; el CI se replicó en Ubuntu 24.04 con la imagen oficial del SDK 10):

- **219 pruebas del agente (202 unitarias y 17 de integración), 219 correctas y ninguna omitida.** Las 73 unitarias nuevas desde US-05 cubren:
  - el publicador HTTP: secuencia, lotes, reintentos, timeouts, redirecciones, 401, freno y abortar;
  - la huella y las opciones de `Api`, con `http` solo en loopback;
  - `--test` con la API;
  - el estado después de un envío confirmado o fallido;
  - el rechazo de la contraseña de ejemplo (`CAMBIAR`) y una clave con `;`, `=` y comillas en la cadena de conexión.
- La API de US-06, del lado del backend: 19 pruebas de integración contra PostgreSQL. "Completar" aplica 18 500 artículos en unos 6 s.
- **Prueba de punta a punta local** (2026-11-25): SQL Server de prueba en Docker (`127.0.0.1,14330`) → agente `--once` → frontend (`localhost:3000`) → backend → PostgreSQL → `/api/products`.
  - `--test`: 14 artículos, 13 activos, 14 códigos distintos, y «Responde y la llave es válida».
  - Primera lectura: los dos códigos que difieren por espacios son productos distintos, `34.999999` queda en `35.00`, los artículos sin precio quedan en `0.00`, la baja inactiva, las descripciones vacías o `NULL` sin nombre y la existencia de 500 000 000 000 se guarda. Se ven 7 productos.
  - Sin cambios en Aspel: la API registra «sin cambios» y no se envía ningún producto.
  - Un cambio de precio viaja y se aplica (`1 actualizados`) y aparece en `/api/products`.
  - Freno de seguridad: con 33 productos DEV activos que no vienen de Aspel, la corrida falla (`Freno de seguridad: se desactivarían 33 de 46…`), el agente sale con el código 5, no cambia nada (ni el precio) y `lastSuccessAt` se conserva.
  - Con `Sync__MaxDeactivationRatio=1` la baja se aplica (`33 desactivados`) y el catálogo vuelve a 7 productos.
  - Llave incorrecta: `--test` y `--once` salen con 5 y la API no crea ninguna corrida.
  - La llave de prueba no aparece en los logs ni en el estado.
- Instalación en Windows 11 con `-UseLocalService` (SID propio de la tarea, permisos, tarea temporal sin SID que **no** puede leer la configuración, y desinstalación sin tocar cuentas): resultados los esperados.
- Instalación, ejecución de la tarea con la cuenta dedicada y sin sesión iniciada (`LastTaskResult 0`, estado en disco, sin la contraseña de SQL en `data\`), reinstalación que conserva configuración y datos, y desinstalación con perfil incluido (tras reiniciar) y segunda ejecución.
- Desinstalación con `-KeepData`: conserva `data\` y la configuración y quita los permisos de la cuenta eliminada, tanto con la cuenta existente como cuando ya se había eliminado antes. Una primera prueba real dejó permisos huérfanos (`icacls` no puede quitar el permiso de una cuenta que ya no existe); se corrigió y se repitió sin errores.
- Cambio de la clave de `catalogo_lectura` (la anterior deja de servir) y rechazo de claves inválidas.
- `Set-CatalogoLecturaLogin.ps1` en una consola interactiva real: la clave tecleada en las líneas ocultas **no** aparece en `ConsoleHost_history.txt` (se buscó con una línea de control que demostró que el historial se estaba guardando). Al buscarla, la única coincidencia era el propio comando de búsqueda con la clave escrita completa, no el script.

**Probado en la tienda** (2026-11-24, por acceso remoto):

- `--test` con `catalogo_lectura` contra la base real: las cifras de la [sección 6](#6-modos-de-uso-y-códigos-de-salida), con el JOIN sin duplicados.
  - Tiempos: conexión 126 ms, consulta 164 ms y 389 ms en total.
  - Memoria: working set máximo de 49.8 MB, con el tope del GC de 64 MB activo.
- Salida HTTPS: TCP 443 a Railway correcta, `/api/health` responde `Healthy` y la primera petición tarda unos 590 ms.
- El instalador anterior falló en el controlador de dominio. Creó `svc-nettes-sync` en el dominio, que ya se borró con `Remove-ADUser`, y luego no encontró el grupo `S-1-5-32-545`. Se corrigió: ver [En un controlador de dominio](#3-instalar-la-tarea-programada).

**Todavía sin probar**

- Los scripts en un **controlador de dominio** (solo se probaron en Windows 11): el aviso de una cuenta `svc-nettes-sync` en el dominio y el flujo completo con LOCAL SERVICE.
- La recuperación cuando alguien cambia la clave de la cuenta de Windows (el procedimiento documentado es volver a ejecutar `Install-SyncAgent.ps1`, que fija la clave y vuelve a registrar la tarea; ya se probó que esa reinstalación actualiza la cuenta y la tarea, pero no el caso exacto de una clave cambiada por otro medio).

**Falta verificar en la tienda**

1. La **instalación con el instalador corregido** (LOCAL SERVICE en el controlador de dominio) y que **no** se cree ninguna cuenta en el dominio: `Get-ADUser -Filter "Name -eq 'svc-nettes-sync'"` no debe devolver nada.
2. El **primer ciclo de la tarea** como LOCAL SERVICE: `LastTaskResult 0`, `sync-state.json` y el log en `data\`.
3. Los **tipos reales de las columnas** (`char` o `varchar`, `float`) y el aislamiento de la base: `sql/install/02`.
4. Los permisos efectivos de `catalogo_lectura`: `sql/install/04`.
5. Que la tarea corre sin sesión iniciada **y después de reiniciar el servidor**.
6. Que no hay reglas de firewall entrantes nuevas.
7. Quién custodia la contraseña de `catalogo_lectura`.
8. Que la API acepta la llave desde el servidor (`--test` → *«Responde y la llave es válida»*).
9. La **primera carga real**: duración del envío y de «completar» con la latencia real y unos 18 665 artículos, y que el freno no se active.
10. Que la llave quedó en `appsettings.Production.json` con los permisos restringidos y que el hash de Railway coincide.

## 16. Estructura de la carpeta

```
sync-agent/
├── SyncAgent.slnx
├── global.json                      # fija el SDK 10 y el ejecutor de pruebas
├── src/SyncAgent/                   # el programa (Aspel/, Diagnostics/, Logging/, Publishing/ = envío a la API, Resilience/, State/)
├── tests/SyncAgent.Tests/           # Unit/ e Integration/
├── scripts/
│   ├── Install-SyncAgent.ps1        # instala o actualiza la tarea programada
│   ├── Uninstall-SyncAgent.ps1      # deshace todo
│   ├── Set-CatalogoLecturaLogin.ps1 # crea o cambia la clave de catalogo_lectura sin escribirla en la línea de comandos
│   └── New-SyncAgentKey.ps1         # genera la llave del agente y muestra su hash para Railway
└── sql/
    ├── dev/                         # base de prueba (solo desarrollo)
    └── install/                     # 01 crear cuenta · 02 inspeccionar · 03 memoria · 04 verificar permisos
```
