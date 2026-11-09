# Agente de sincronización con Aspel SAE (US-05)

Programa de .NET 10 que corre en el servidor de la tienda y **lee** la base de Aspel SAE (SQL Server) en modo de solo lectura. Aspel SAE nunca se expone a Internet: el agente es el único que lo consulta, y lo hace desde el mismo servidor.

> **Alcance de esta historia (US-05):** el agente lee, resume y guarda su estado en disco. **Todavía no envía datos a la API**: eso es US-06. El punto donde se conectará ya existe (`ICatalogPublisher`).

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
- Guarda su estado en `data\sync-state.json` y sus registros en `data\logs\` (se borran a los 14 días).
- Tiene un modo de prueba (`--test`) que resume lo leído y mide tiempos y memoria, sin enviar ni guardar nada.

**No hace**

- No escribe en Aspel: la cuenta `catalogo_lectura` solo tiene `SELECT` sobre dos tablas.
- No usa `NOLOCK`: puede devolver filas duplicadas u omitidas. Lee con el aislamiento por defecto (`READ COMMITTED`); con unas 18 mil filas la lectura dura milisegundos.
- No abre ningún puerto: no recibe conexiones.
- No envía datos a la API (US-06).

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
│                         └──► (US-06) HTTPS saliente ──┼──► API (Railway)
└──────────────────────────────────────────────────────┘
```

**Por qué una tarea programada y no un servicio de Windows.** El servidor tiene solo 2 GB de RAM compartidos con SQL Server y Aspel.

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

El agente lee `appsettings.json` (valores de fábrica, sin secretos), luego `appsettings.Production.json` y, en desarrollo, los *User Secrets*. **El único valor obligatorio es la cadena de conexión** (`ConnectionStrings:AspelSae`). Si falta o está mal escrita, el programa termina con el código 1 y lo dice.

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

### En la tienda: `appsettings.Production.json`

Un archivo junto al `.exe`, **fuera de git** (está en `.gitignore`). El repositorio solo trae [`appsettings.Production.example.json`](src/SyncAgent/appsettings.Production.example.json), con la contraseña `CAMBIAR`. El instalador lo copia y le restringe los permisos (ver [paso 4 de la instalación](#4-configurar-la-conexión)). Se prefiere un archivo a las variables de entorno de máquina porque estas las puede leer cualquier proceso del servidor, mientras que el archivo solo lo leen Administradores, SYSTEM y la cuenta del agente.

```json
{
  "ConnectionStrings": {
    "AspelSae": "Server=localhost;Database=SAE70Empre01;User Id=catalogo_lectura;Password=<la clave>;TrustServerCertificate=True;"
  },
  "Api": { "BaseUrl": "", "AgentKey": "" }
}
```

La sección `Api` queda preparada para US-06 (la clave propia del agente irá en este mismo archivo).

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

Solo se reintentan los fallos transitorios. Un inicio de sesión fallido (error 18456) **no** se reintenta: repetirlo solo bloquearía la cuenta.

## 6. Modos de uso y códigos de salida

```powershell
SyncAgent.exe --test     # una lectura y un resumen en pantalla; no envía ni guarda nada
SyncAgent.exe --once     # una sincronización completa (la que ejecuta la tarea programada)
```

`--test` es lo que se usa en la tienda para validar la consulta contra la base real. Solo escribe en la consola (no crea archivos), de modo que se puede ejecutar a mano como Administrador sin ensuciar `data\`.

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
```

**Cómo leerlo en la tienda.** Las cifras de la exportación de noviembre de 2026 (unos 18 460 artículos, unos 121 activos sin precio, unos 14 pares de códigos con espacios) son **aproximadas**: el inventario cambia. Una diferencia pequeña es normal. Lo que importa es:

- que **«Códigos distintos» sea igual al total de filas** (si es menor, la consulta duplica artículos y hay que revisarla);
- que los **activos sin precio aparezcan** (no se pierden);
- que haya **pares de códigos con espacios** (se entregan como productos distintos);
- los **tiempos** y la **memoria máxima** reales del servidor.

`--once` guarda `data\sync-state.json` (resultado de la última ejecución, **última sincronización exitosa**, fallos seguidos, artículos leídos, duración y memoria) y escribe `data\logs\agent-AAAA-MM-DD.log`. Un fallo suma el contador pero **no borra** la fecha de la última sincronización exitosa. Una segunda ejecución simultánea no se solapa: sale de inmediato con el código 3.

| Código | Significado | Qué hacer |
|---|---|---|
| `0` | Correcto | — |
| `1` | Configuración inválida o incompleta | Leer el mensaje: dice cuál valor falta o sobra. |
| `2` | No se pudo leer SQL Server (después de los reintentos) | Ver la tabla de [solución de problemas](#12-solución-de-problemas) por número de error. |
| `3` | Ya hay otra ejecución en curso | Normal si una ejecución tarda más que el intervalo; la tarea la ignora. |
| `4` | Error inesperado | Ver el log de `data\logs\`. |

El Programador de tareas muestra este código en *«Último resultado»*.

## 7. Pruebas

Desde `sync-agent\` (con el SDK 10):

```powershell
dotnet test --solution SyncAgent.slnx -c Release
```

Sin base de datos corren las **pruebas unitarias** (129) y el resumen de `dotnet test` muestra `total: 141`, `succeeded: 129` y `skipped: 12` (una prueba con 6 variantes cuenta como una sola cuando se omite). Las **17 pruebas de integración** (`Category=Integration`) usan la base de Docker y **se omiten solas** si no existe la variable `SYNCAGENT_TEST_CONNECTION`:

```powershell
$p = ((Get-Content ..\.env | Where-Object { $_ -like 'CATALOGO_LECTURA_PASSWORD=*' }) -replace '^CATALOGO_LECTURA_PASSWORD=','').Trim()
$env:SYNCAGENT_TEST_CONNECTION = "Server=127.0.0.1,14330;Database=SAE70Empre01;User Id=catalogo_lectura;Password=$p;TrustServerCertificate=True;"
dotnet test --solution SyncAgent.slnx -c Release       # ahora: total: 146, succeeded: 146, skipped: 0
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

Pide la contraseña de la cuenta de Windows `svc-nettes-sync` (dos veces) y hace, mostrando cada paso:

1. Verifica que es administrador y que existe la carpeta publicada.
2. Crea la **cuenta local dedicada**, sin privilegios de administrador, y le concede **un único derecho especial**: «Iniciar sesión como trabajo por lotes». Windows **no** lo concede solo; sin él la tarea nunca arranca (ver [solución de problemas](#12-solución-de-problemas)).
3. Copia el programa a `C:\NettesSyncAgent` sin tocar la configuración ni los datos que ya existan.
4. Aplica permisos NTFS de mínimo privilegio (tabla siguiente).
5. Crea `appsettings.Production.json` a partir del ejemplo y lo protege.
6. Registra la tarea programada.
7. Muestra un resumen y los comandos para verificar.

Se puede ejecutar varias veces: lo que ya existe se **actualiza**. `Get-Help .\Install-SyncAgent.ps1 -Full` muestra todas las opciones (`-InstallPath`, `-AccountName`, `-IntervalMinutes`…).

**Permisos NTFS**

| | `C:\NettesSyncAgent` | `data\` | `appsettings.Production.json` |
|---|---|---|---|
| Administradores y SYSTEM | Control total | Control total | Control total |
| Cuenta `svc-nettes-sync` | Lectura y ejecución | **Modificar** (única carpeta donde escribe) | Solo lectura |
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

Reemplace `CAMBIAR` por la clave de `catalogo_lectura`. Como esa clave solo lleva letras, números y `- _ . ! * + @`, no hace falta escapar nada ni usar comillas dentro de ella.

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

### 7. Ejecutar la tarea una vez y verificar

```powershell
Start-ScheduledTask -TaskPath '\NettesSyncAgent\' -TaskName 'Sincronizacion'
Start-Sleep -Seconds 12
Get-ScheduledTask -TaskPath '\NettesSyncAgent\' | Get-ScheduledTaskInfo | Format-List LastRunTime,LastTaskResult,NextRunTime
Get-Content C:\NettesSyncAgent\data\sync-state.json
Get-Content (Get-ChildItem C:\NettesSyncAgent\data\logs\*.log | Select-Object -Last 1) -Tail 8
```

Debe mostrar `LastTaskResult : 0`, `"lastResult": "succeeded"` y los artículos leídos. A partir de ahí la tarea corre sola cada 15 minutos y después de cada reinicio. Para ver permisos: `icacls C:\NettesSyncAgent` y `icacls C:\NettesSyncAgent\data`.

## 10. Contraseñas

Hay **dos** y no se confunden.

| | Para qué sirve | Dónde vive | Quién la guarda |
|---|---|---|---|
| `catalogo_lectura` (SQL Server) | El agente se conecta con ella a la base de Aspel. | Solo en `appsettings.Production.json` del servidor, con permisos restringidos. | La persona responsable del servidor de la tienda, en su gestor de contraseñas. |
| `svc-nettes-sync` (cuenta de Windows) | La tarea inicia sesión con ella sin que nadie esté conectado. | Solo la conocen Windows y el Programador de tareas; no se escribe en ningún archivo. | La misma persona. Nadie la necesita en el día a día. |

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
| *«The term '.\scripts\…' is not recognized»* | La ventana de PowerShell abrió en `C:\Windows\System32`. | `Set-Location` a la carpeta de los scripts. |
| *«running scripts is disabled on this system»* | Política de ejecución de scripts. | `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force` (solo vale para esa ventana). |
| *«A compatible .NET SDK was not found»* al compilar | No está instalado el SDK 10. | `winget install --id Microsoft.DotNet.SDK.10 -e` |
| El perfil `C:\Users\svc-nettes-sync` sigue ahí después de desinstalar | Windows lo mantiene cargado. | Reinicie el equipo y vuelva a ejecutar `Uninstall-SyncAgent.ps1`. |

**Dónde mirar:** `C:\NettesSyncAgent\data\logs\agent-AAAA-MM-DD.log` (uno por día, 14 días) y `C:\NettesSyncAgent\data\sync-state.json`. Los mensajes de error de SQL se traducen a una explicación en español y el usuario y la contraseña nunca se escriben.

## 13. Seguridad y criterios de aceptación

| Criterio de US-05 | Estado | Evidencia |
|---|---|---|
| El agente recupera productos, precios y estado correctamente | Cumplido contra la base de prueba; falta la base real | 17 pruebas de integración y `--test` sobre los 14 casos; 18 460 artículos en una carga temporal |
| Los productos sin precio con `CVE_PRECIO = 1` no se pierden | Cumplido (prueba) | `LEFT JOIN`; con `INNER JOIN` fallan 6 pruebas; llegan con precio vacío |
| El agente no modifica datos de Aspel | Cumplido (prueba) | Solo hay un `SELECT` (una prueba lo exige); `INSERT`/`UPDATE`/`DELETE` con `catalogo_lectura` fallan con el error 229 en las dos tablas; la cuenta no pertenece a ningún rol |
| Las credenciales no se exponen en el código ni en los logs | Cumplido (prueba) | Excepciones de SqlClient reales sin la clave; el mensaje de login fallido sale como `Login failed for user '***'`; búsqueda de la clave en `data\`, logs y salida: sin coincidencias |
| No se abre ningún puerto de SQL Server hacia Internet | Cumplido por diseño; falta comprobarlo en la tienda | El agente usa `localhost` y solo hace conexiones salientes (US-06); el instalador no crea reglas de firewall |

## 14. Compatibilidad y riesgos

- **.NET 10 y Windows Server 2016.** La [matriz oficial de sistemas soportados de .NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) incluye Windows Server 2016 x64.
- **Riesgo del sistema operativo.** El soporte de Windows Server 2016 termina el **12 de enero de 2027** ([ciclo de vida de Microsoft](https://learn.microsoft.com/lifecycle/products/windows-server-2016)). La misma matriz aclara que las versiones sin soporte del fabricante del sistema no se prueban ni se soportan en .NET. El agente seguirá funcionando después de esa fecha, pero sin garantía oficial; el riesgo es del sistema operativo de la tienda, no del agente.
- **Espacio en disco.** El programa ocupa ~89 MB y los logs se conservan 14 días como máximo.

## 15. Qué se probó y qué falta verificar en la tienda

**Probado** (portátil con Windows 11, SQL Server 2022 en Docker, SDK 10.0.403; el CI se replicó en Ubuntu 24.04 con la imagen oficial del SDK 10):

- 129 pruebas unitarias y 17 de integración.
- Instalación, ejecución de la tarea con la cuenta dedicada y sin sesión iniciada (`LastTaskResult 0`, estado en disco, sin la contraseña de SQL en `data\`), reinstalación que conserva configuración y datos, y desinstalación con perfil incluido (tras reiniciar) y segunda ejecución.
- Cambio de la clave de `catalogo_lectura` (la anterior deja de servir) y rechazo de claves inválidas.

**Todavía sin probar**

- `Uninstall-SyncAgent.ps1 -KeepData`.
- La recuperación cuando alguien cambia la clave de la cuenta de Windows.
- Que `Set-CatalogoLecturaLogin.ps1` no deje la clave en el historial en una consola interactiva real (por diseño no debe).

**Falta verificar en la tienda**

1. Los **tipos reales de las columnas** (`char` o `varchar`, `float`), la colación y el aislamiento de la base: `sql/install/02`.
2. Que el resumen de `--test` se parezca a la exportación de noviembre de 2026 (cifras **aproximadas**): códigos distintos igual al total, activos sin precio presentes, pares de códigos con espacios.
3. La **duración de la consulta** y la **memoria máxima** reales, con Aspel y SQL Server trabajando.
4. `max server memory` de SQL Server: `sql/install/03`.
5. Los permisos efectivos de `catalogo_lectura`: `sql/install/04`.
6. Que la tarea corre sin sesión iniciada **y después de reiniciar el servidor**.
7. Que no hay reglas de firewall entrantes nuevas.
8. Quién custodia las dos contraseñas.

## 16. Estructura de la carpeta

```
sync-agent/
├── SyncAgent.slnx
├── global.json                      # fija el SDK 10 y el ejecutor de pruebas
├── src/SyncAgent/                   # el programa (Aspel/, Diagnostics/, Logging/, Resilience/, State/, Publishing/)
├── tests/SyncAgent.Tests/           # Unit/ e Integration/
├── scripts/
│   ├── Install-SyncAgent.ps1        # instala o actualiza la tarea programada
│   ├── Uninstall-SyncAgent.ps1      # deshace todo
│   └── Set-CatalogoLecturaLogin.ps1 # crea o cambia la clave de catalogo_lectura sin escribirla en la línea de comandos
└── sql/
    ├── dev/                         # base de prueba (solo desarrollo)
    └── install/                     # 01 crear cuenta · 02 inspeccionar · 03 memoria · 04 verificar permisos
```
