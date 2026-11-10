<#
.SYNOPSIS
    Crea (o cambia la clave de) la cuenta de SQL Server 'catalogo_lectura' SIN escribir la clave en la línea de comandos.

.DESCRIPTION
    Ejecuta sql\install\01-crear-login-lectura.sql, que crea la cuenta de solo lectura del agente (SELECT únicamente sobre
    dbo.INVE01 y dbo.PRECIO_X_PROD01). Si la cuenta ya existe, solo le cambia la clave (no hace falta la anterior).

    POR QUÉ EXISTE ESTE SCRIPT
      Pasar la clave con "sqlcmd -v LoginPassword=..." la deja escrita en el historial de PowerShell (ConsoleHost_history.txt)
      y visible en la lista de procesos mientras corre. Este script la pide con Read-Host -AsSecureString (se teclea en
      una línea oculta, no es un comando, no entra al historial) y NO lanza ningún programa: usa el cliente SQL que ya trae
      Windows PowerShell 5.1, así que la clave no aparece en ninguna línea de comandos ni en ninguna lista de procesos.

    QUÉ HACE (4 pasos, cada uno se muestra en pantalla)
      1. Pide la clave dos veces y la VALIDA antes de tocar nada.
      2. Se conecta a SQL Server con una cuenta administradora.
      3. Ejecuta el archivo SQL (el mismo que usa el entorno de desarrollo) y muestra sus mensajes y los permisos que quedaron.
      4. Comprueba la clave: se conecta como 'catalogo_lectura' y cuenta los artículos de INVE01 (así un error de tecleo
         se detecta en el momento).

    REGLAS DE LA CLAVE
      - Mínimo 12 caracteres, con mayúscula, minúscula, número y símbolo.
      - Solo letras (A-Z, a-z), números (0-9) y estos símbolos:  -  _  .  !  *  +  @
      - NO se permiten: comilla simple ('), comilla doble ("), punto y coma (;), signo de dólar ($), barra invertida (\) ni
        espacios. Motivo: la clave se escribe después como TEXTO en la cadena de conexión de appsettings.Production.json
        (donde ; separa opciones y las comillas agrupan valores, y como es un archivo JSON, " y \ también son especiales)
        y en el script SQL, donde una ' cierra el texto y $( se interpreta como una variable.

    CÓMO SE TECLEA: cuando el script la pida, escriba la clave SIN comillas y pulse Enter (no se ve lo que escribe); luego
    repítala. Este script no necesita permisos de administrador de Windows, pero sí una cuenta con permiso de crear
    inicios de sesión en SQL Server (sysadmin).

    SI POR ERROR ESCRIBIÓ UNA CLAVE DENTRO DE UN COMANDO: trátela como filtrada, cámbiela con este script y borre esa línea
    del historial:
        $h = (Get-PSReadLineOption).HistorySavePath
        (Get-Content $h) | Where-Object { $_ -notmatch 'LoginPassword|AsPlainText|-AccountPassword' } | Set-Content $h -Encoding UTF8
    y cierre esa ventana de PowerShell.

.PARAMETER Server
    Servidor SQL: 'localhost' (por omisión), 'servidor\instancia' o 'host,puerto' (p. ej. 127.0.0.1,14330 para la base de prueba de Docker).

.PARAMETER SqlAdminUser
    Cuenta administradora de SQL Server (p. ej. sa). Si se omite se usa la autenticación de Windows del usuario actual.
    Su clave se pide también con Read-Host -AsSecureString.

.PARAMETER SqlAdminPassword
    Solo para automatizar y probar. Debe ser un SecureString; NO lo construya con texto escrito en la línea de comandos.

.PARAMETER LoginPassword
    Solo para automatizar y probar. Debe ser un SecureString; NO lo construya con texto escrito en la línea de comandos.

.PARAMETER SqlFile
    Archivo SQL a ejecutar. Por omisión: ..\sql\install\01-crear-login-lectura.sql junto a este script.

.EXAMPLE
    .\Set-CatalogoLecturaLogin.ps1

.EXAMPLE
    .\Set-CatalogoLecturaLogin.ps1 -Server 'localhost\SQLEXPRESS' -SqlAdminUser sa
#>
# (Esta línea va DESPUÉS del bloque de ayuda a propósito: si va antes, PowerShell 5.1 no reconoce la ayuda de Get-Help.)
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Server = 'localhost',
    [string]$SqlAdminUser,
    [System.Security.SecureString]$SqlAdminPassword,
    [System.Security.SecureString]$LoginPassword,
    [string]$SqlFile = (Join-Path $PSScriptRoot '..\sql\install\01-crear-login-lectura.sql')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$LoginName = 'catalogo_lectura'
$TotalSteps = 4

function Write-Step([int]$Number, [string]$Text) { Write-Host ''; Write-Host "[$Number/$TotalSteps] $Text" -ForegroundColor Cyan }
function Write-Info([string]$Text) { Write-Host "      $Text" }
function Write-Ok([string]$Text) { Write-Host "      OK     $Text" -ForegroundColor Green }
function Write-Warn([string]$Text) { Write-Host "      AVISO  $Text" -ForegroundColor Yellow }

function ConvertTo-PlainText([System.Security.SecureString]$Secure) {
    # Solo en memoria, para validar y sustituir en el SQL; nunca se imprime ni se escribe en disco.
    return (New-Object System.Net.NetworkCredential('', $Secure)).Password
}

function Test-LoginPasswordRules([string]$Plain) {
    # Devuelve la lista de problemas (vacía si la clave es válida). Nunca incluye la clave completa en los mensajes.
    $problems = New-Object System.Collections.Generic.List[string]
    if ($Plain.Length -lt 12) {
        $problems.Add("es demasiado corta: tiene $($Plain.Length) caracteres y se necesitan al menos 12.")
    }

    $bad = @($Plain.ToCharArray() | Where-Object { [string]$_ -notmatch '^[A-Za-z0-9\-_.!*+@]$' } | Select-Object -Unique)
    if ($bad.Count -gt 0) {
        $shown = @($bad | ForEach-Object { if ($_ -eq ' ') { 'espacio' } else { "'" + $_ + "'" } })
        $problems.Add('contiene caracteres no permitidos: ' + ($shown -join ' ') + '. Use solo letras, números y - _ . ! * + @')
    }

    if ($Plain -cnotmatch '[A-Z]') { $problems.Add('le falta una letra mayúscula.') }
    if ($Plain -cnotmatch '[a-z]') { $problems.Add('le falta una letra minúscula.') }
    if ($Plain -notmatch '[0-9]') { $problems.Add('le falta un número.') }
    if ($Plain -notmatch '[\-_.!*+@]') { $problems.Add('le falta un símbolo (uno de - _ . ! * + @).') }
    return $problems.ToArray()
}

function ConvertTo-SqlLiteralContent([string]$Text) {
    # Dentro de un texto de T-SQL ('...') la comilla simple se escribe doble. Así ningún carácter puede cerrar el texto.
    return $Text.Replace("'", "''")
}

function Split-SqlBatches([string]$Text) {
    # Parte el script en lotes en las líneas que solo dicen GO, como hace sqlcmd. Un GO dentro de un comentario de bloque
    # (/* ... */) NO separa lotes: el archivo trae, comentado, el bloque "para deshacer" con varias líneas GO.
    $batches = New-Object System.Collections.Generic.List[string]
    $current = New-Object System.Text.StringBuilder
    $inComment = $false

    foreach ($line in ($Text -split "\r?\n")) {
        if ((-not $inComment) -and ($line -match '^\s*GO\s*$')) {
            if ($current.Length -gt 0) { $batches.Add($current.ToString()); [void]$current.Clear() }
            continue
        }
        [void]$current.AppendLine($line)
        foreach ($token in [regex]::Matches($line, '/\*|\*/')) { $inComment = ($token.Value -eq '/*') }
    }
    if ($current.Length -gt 0) { $batches.Add($current.ToString()) }

    # Se descartan los lotes que solo traen comentarios o espacios.
    return @($batches | Where-Object {
        $withoutComments = [regex]::Replace([regex]::Replace($_, '(?s)/\*.*?\*/', ''), '--[^\r\n]*', '')
        $withoutComments.Trim().Length -gt 0
    })
}

function Hide-Secret([string]$Text) {
    # Última defensa: si un mensaje de error llegara a repetir la clave, se oculta antes de mostrarlo.
    if (-not $Text) { return $Text }
    foreach ($secret in $script:SecretsToHide) { if ($secret) { $Text = $Text.Replace($secret, '***') } }
    return $Text
}

$script:SecretsToHide = @()
$script:InfoMessages = New-Object System.Collections.Generic.List[string]
$plain = $null

try {
    # =================================================================================================================
    Write-Step 1 'Clave nueva para la cuenta y validación'
    # =================================================================================================================
    if ($Server -notmatch '^[A-Za-z0-9._\\,:\-]+$') {
        throw "El nombre del servidor tiene caracteres no válidos: $Server"
    }
    if (-not (Test-Path -LiteralPath $SqlFile -PathType Leaf)) {
        throw "No se encuentra el archivo SQL: $SqlFile (¿copió también la carpeta sql\ junto a scripts\?)"
    }
    $SqlFile = (Resolve-Path -LiteralPath $SqlFile).Path

    if ($null -eq $LoginPassword) {
        Write-Info "Reglas: mínimo 12 caracteres, con mayúscula, minúscula, número y símbolo."
        Write-Info "Solo letras, números y estos símbolos:  -  _  .  !  *  +  @      (sin comillas, ;, `$, \ ni espacios)"
        $LoginPassword = Read-Host -AsSecureString -Prompt "Clave NUEVA para '$LoginName'"
        $confirmation = Read-Host -AsSecureString -Prompt 'Repita la clave'
        if ((ConvertTo-PlainText $LoginPassword) -cne (ConvertTo-PlainText $confirmation)) {
            throw 'Las dos claves no coinciden. No se hizo ningún cambio.'
        }
    }

    $plain = ConvertTo-PlainText $LoginPassword
    $problems = @(Test-LoginPasswordRules $plain)
    if ($problems.Count -gt 0) {
        throw ("La clave no es válida:`n      - " + ($problems -join "`n      - ") + "`n      No se hizo ningún cambio.")
    }
    $script:SecretsToHide = @($plain, (ConvertTo-SqlLiteralContent $plain))
    Write-Ok 'La clave cumple las reglas.'

    if ($SqlAdminUser -and ($null -eq $SqlAdminPassword)) {
        $SqlAdminPassword = Read-Host -AsSecureString -Prompt "Clave de la cuenta administradora de SQL '$SqlAdminUser'"
    }

    # El archivo SQL usa un único marcador, $(LoginPassword). Si trajera otro, no se sabría qué poner.
    $sqlText = [IO.File]::ReadAllText($SqlFile, [Text.Encoding]::UTF8)
    $markers = @([regex]::Matches($sqlText, '\$\(\w+\)') | ForEach-Object { $_.Value } | Select-Object -Unique)
    if (($markers.Count -ne 1) -or ($markers[0] -ne '$(LoginPassword)')) {
        throw "El archivo SQL debe tener un único marcador `$(LoginPassword); tiene: $($markers -join ', ')"
    }
    $sqlText = $sqlText.Replace('$(LoginPassword)', (ConvertTo-SqlLiteralContent $plain))
    $batches = @(Split-SqlBatches $sqlText)
    Write-Ok "Archivo SQL leído: $($batches.Count) lotes."

    # =================================================================================================================
    Write-Step 2 "Conectar a SQL Server ($Server)"
    # =================================================================================================================
    # Encrypt=True y TrustServerCertificate=True: la conexión va cifrada aunque el servidor use un certificado propio
    # (es lo habitual en una base local). La clave del administrador viaja en un SqlCredential (SecureString), no dentro
    # de la cadena de conexión.
    $baseConnection = "Data Source=$Server;Initial Catalog=master;Encrypt=True;TrustServerCertificate=True;Connect Timeout=15;Application Name=Set-CatalogoLecturaLogin"
    if ($SqlAdminUser) {
        $adminSecure = $SqlAdminPassword.Copy()
        $adminSecure.MakeReadOnly()
        $credential = New-Object System.Data.SqlClient.SqlCredential($SqlAdminUser, $adminSecure)
        $connection = New-Object System.Data.SqlClient.SqlConnection($baseConnection, $credential)
        $who = "cuenta SQL '$SqlAdminUser'"
    }
    else {
        $connection = New-Object System.Data.SqlClient.SqlConnection("$baseConnection;Integrated Security=SSPI")
        $who = "autenticación de Windows ($([Security.Principal.WindowsIdentity]::GetCurrent().Name))"
    }
    $connection.add_InfoMessage({ param($sender, $e) $script:InfoMessages.Add([string]$e.Message) })
    $connection.Open()
    Write-Ok "Conectado con $who."

    # =================================================================================================================
    Write-Step 3 'Ejecutar sql\install\01-crear-login-lectura.sql'
    # =================================================================================================================
    try {
        foreach ($batch in $batches) {
            $command = $connection.CreateCommand()
            $command.CommandText = $batch
            $command.CommandTimeout = 60
            $reader = $command.ExecuteReader()
            try {
                do {
                    $table = New-Object System.Data.DataTable
                    $table.Load($reader)
                    if (($table.Columns.Count -gt 0) -and ($table.Rows.Count -gt 0)) {
                        Write-Info 'Permisos que quedaron para la cuenta:'
                        (($table | Format-Table -AutoSize | Out-String).TrimEnd() -split "\r?\n") | ForEach-Object { Write-Host "        $_" }
                    }
                } while (-not $reader.IsClosed)
            }
            finally { if (-not $reader.IsClosed) { $reader.Close() } }

            foreach ($message in $script:InfoMessages) { Write-Info (Hide-Secret $message) }
            $script:InfoMessages.Clear()
        }
    }
    finally { $connection.Close() }
    Write-Ok 'El script SQL terminó sin errores.'

    # =================================================================================================================
    Write-Step 4 "Comprobar la clave: conectar como '$LoginName' y leer INVE01"
    # =================================================================================================================
    $loginSecure = $LoginPassword.Copy()
    $loginSecure.MakeReadOnly()
    $loginCredential = New-Object System.Data.SqlClient.SqlCredential($LoginName, $loginSecure)
    $check = New-Object System.Data.SqlClient.SqlConnection(
        "Data Source=$Server;Initial Catalog=SAE70Empre01;Encrypt=True;TrustServerCertificate=True;Connect Timeout=15;Application Name=Set-CatalogoLecturaLogin", $loginCredential)
    try {
        $check.Open()
        $countCommand = $check.CreateCommand()
        $countCommand.CommandText = 'SELECT COUNT(*) FROM dbo.INVE01'
        $count = $countCommand.ExecuteScalar()
    }
    finally { $check.Close() }
    Write-Ok "La cuenta '$LoginName' inicia sesión con la clave nueva y lee dbo.INVE01 ($count artículos)."

    Write-Host ''
    Write-Host 'PRÓXIMO PASO' -ForegroundColor Cyan
    Write-Host "      Ponga esta misma clave en appsettings.Production.json del agente (editándolo como administrador), en:"
    Write-Host "        ConnectionStrings > AspelSae > ...User Id=$LoginName;Password=<la clave>;..."
    Write-Host '      Luego compruebe con:  SyncAgent.exe --test'
    Write-Host ''
    Write-Host 'Listo.' -ForegroundColor Green
    exit 0
}
catch {
    $message = $_.Exception.Message

    # PowerShell envuelve las excepciones de los métodos de .NET (por ejemplo "Exception calling Open...") dentro de otra
    # excepción, así que la SqlException real está en una excepción INTERNA. Se recorre la cadena hasta encontrarla.
    $sqlError = $_.Exception
    while (($null -ne $sqlError) -and -not ($sqlError -is [System.Data.SqlClient.SqlException])) { $sqlError = $sqlError.InnerException }

    if ($null -ne $sqlError) {
        $message = $sqlError.Message
        switch ($sqlError.Number) {
            15118 { $message = 'SQL Server rechazó la clave por su directiva de contraseñas (complejidad o historial). Pruebe con una más larga y variada.' }
            18456 { $message = 'Falló el inicio de sesión: usuario o clave incorrectos, o la cuenta no tiene permiso para conectarse.' }
            911   { $message = 'La base SAE70Empre01 no existe en ese servidor. Este script se ejecuta en el servidor donde está Aspel.' }
            15247 { $message = 'La cuenta administradora no tiene permiso para crear inicios de sesión (hace falta el rol sysadmin).' }
            { $_ -in 2, 40, 53, 121, 233, 258, 1225, 10053, 10054, 10060, 10061, 11001, -1 } { $message = "No se pudo conectar al servidor '$Server': revise el nombre, el puerto y que SQL Server esté encendido." }
            default { }
        }
        $message = "[SQL $($sqlError.Number)] $message"
    }
    Write-Host ''
    Write-Host 'ERROR: no se completó.' -ForegroundColor Red
    Write-Host (Hide-Secret $message) -ForegroundColor Red
    exit 1
}
finally {
    $plain = $null
    $script:SecretsToHide = @()
}
