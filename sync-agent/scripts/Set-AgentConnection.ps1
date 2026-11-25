<#
.SYNOPSIS
    Escribe en la configuración del agente la cadena de conexión a SQL Server (con la contraseña de catalogo_lectura) y,
    si se indica, la URL de la API. La contraseña se pide en una línea oculta: nunca se escribe en un comando, ni en el
    historial de PowerShell, ni en pantalla.

.DESCRIPTION
    Qué hace, en orden:

      1. Pide la contraseña con Read-Host -AsSecureString (dos veces, para evitar un error de tecleo).
         LA ESCRIBE LA PERSONA QUE LA CUSTODIA, durante la sesión, en su propia ventana. No se envía por chat ni por correo ni
         se pega en un comando.
      2. Arma la cadena con SqlConnectionStringBuilder, no concatenando texto: una clave con ; = o comillas no rompe la cadena.
      3. Prueba el inicio de sesión ANTES de guardar. Si falla (clave incorrecta, servidor apagado...) no cambia nada.
         Con -SkipConnectionTest se omite la prueba (por ejemplo, si SQL Server todavía no está disponible).
      4. Escribe la cadena (y Api:BaseUrl, si se indicó) SOBRE EL MISMO archivo, como New-SyncAgentKey.ps1: así conserva la lista de
         permisos que puso el instalador. Compara los permisos antes y después y, si cambiaran, los restaura.

    Se ejecuta en una consola de administrador (el archivo solo lo pueden modificar Administradores y SYSTEM). Se puede repetir:
    reemplaza la cadena anterior. Nunca muestra la contraseña ni la cadena de conexión.

.PARAMETER ConfigPath
    appsettings.Production.json del agente instalado. Por omisión: C:\NettesSyncAgent\appsettings.Production.json

.PARAMETER Server
    Servidor y puerto de SQL Server. Por omisión: localhost (el agente corre en el mismo servidor que Aspel).
    En la base de prueba de Docker: 127.0.0.1,14330

.PARAMETER Database
    Base de Aspel. Por omisión: SAE70Empre01

.PARAMETER UserId
    Cuenta de solo lectura. Por omisión: catalogo_lectura

.PARAMETER BaseUrl
    URL pública del sitio (https://<dominio>.up.railway.app). Opcional: si se indica, se guarda en Api:BaseUrl.
    Debe usar https; http solo se acepta con localhost, 127.0.0.1 o ::1 (desarrollo).

.PARAMETER Password
    Contraseña como SecureString. LO NORMAL es NO usar este parámetro: si no se indica, el script la pide por pantalla. Existe
    solo para pruebas automáticas; no la construya con texto escrito en la línea de comandos.

.PARAMETER SkipConnectionTest
    No probar el inicio de sesión antes de guardar.

.EXAMPLE
    .\Set-AgentConnection.ps1 -BaseUrl https://mi-sitio.up.railway.app

.EXAMPLE
    .\Set-AgentConnection.ps1 -ConfigPath C:\Temp\copia.json -Server 127.0.0.1,14330
#>
# (Esta línea va DESPUÉS del bloque de ayuda a propósito: si va antes, PowerShell 5.1 no reconoce la ayuda de Get-Help.)
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ConfigPath = 'C:\NettesSyncAgent\appsettings.Production.json',
    [string]$Server = 'localhost',
    [string]$Database = 'SAE70Empre01',
    [string]$UserId = 'catalogo_lectura',
    [string]$BaseUrl,
    [System.Security.SecureString]$Password,
    [switch]$SkipConnectionTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Ok([string]$Text) { Write-Host "OK     $Text" -ForegroundColor Green }
function Write-Warn([string]$Text) { Write-Host "AVISO  $Text" -ForegroundColor Yellow }

function ConvertTo-PlainText([System.Security.SecureString]$Secure) {
    # Solo para armar la cadena y compararla; se suelta de inmediato y no se escribe en pantalla ni en disco.
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function Get-ConfigValue($Object, [string]$Name) {
    # Lee una propiedad de un objeto de ConvertFrom-Json sin fallar bajo Set-StrictMode si no existe: devuelve $null.
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-OrAddSection($Config, [string]$Name) {
    $section = Get-ConfigValue $Config $Name
    if ($section -isnot [System.Management.Automation.PSCustomObject]) {
        $section = [pscustomobject]@{}
        $Config | Add-Member -NotePropertyName $Name -NotePropertyValue $section -Force
    }
    return $section
}

function Set-ConfigValue($Section, [string]$Name, $Value) {
    if ($null -eq $Section.PSObject.Properties[$Name]) { $Section | Add-Member -NotePropertyName $Name -NotePropertyValue $Value }
    else { $Section.$Name = $Value }
}

function Describe-SqlError([int]$Number) {
    # Explicación en español por NÚMERO de error: el texto de SqlClient puede incluir el usuario.
    switch ($Number) {
        18456 { return 'inicio de sesión rechazado: usuario o clave incorrectos, o la cuenta está deshabilitada.' }
        4060 { return 'la cuenta no puede abrir la base indicada: revise el nombre de la base.' }
        { $_ -in 53, 258, 10060, 10061, -1, 2, 40 } { return 'SQL Server no responde: revise que esté encendido y el valor de -Server.' }
        default { return 'error de SQL Server (consulte el número en la documentación).' }
    }
}

# ---------- Configuración ----------
$fullPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ConfigPath)
if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
    throw "No existe $fullPath. Instale primero el agente (Install-SyncAgent.ps1)."
}

if ($PSBoundParameters.ContainsKey('BaseUrl')) {
    $uri = $null
    if (-not [Uri]::TryCreate($BaseUrl.Trim(), [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -notin 'https', 'http' `
            -or $uri.Query -or $uri.Fragment -or $uri.UserInfo -or $BaseUrl -match '[<>]') {
        throw 'BaseUrl no es una URL válida (ejemplo: https://mi-sitio.up.railway.app, sin ? ni #).'
    }
    if ($uri.Scheme -eq 'http' -and -not $uri.IsLoopback) {
        throw 'BaseUrl debe usar https (http solo se admite con localhost, 127.0.0.1 o ::1, para desarrollo).'
    }
}

# ---------- Contraseña ----------
if ($null -eq $Password) {
    Write-Host "Contraseña de '$UserId' (la escribe la persona que la custodia; no se ve al escribir)."
    $Password = Read-Host -AsSecureString -Prompt "Contraseña de '$UserId'"
    $confirmation = Read-Host -AsSecureString -Prompt 'Repita la contraseña'
    $same = (ConvertTo-PlainText $Password) -ceq (ConvertTo-PlainText $confirmation)
    $confirmation = $null
    if (-not $same) { throw 'Las contraseñas no coinciden. No se cambió nada.' }
}

$plain = ConvertTo-PlainText $Password
try {
    if ([string]::IsNullOrEmpty($plain)) { throw 'La contraseña no puede estar vacía.' }
    if ($plain.Trim() -ieq 'CAMBIAR') { throw 'Esa es la contraseña de ejemplo (CAMBIAR): escriba la contraseña real.' }

    # Con SqlConnectionStringBuilder, una clave con ; = o comillas queda bien entrecomillada. Se asigna por nombre de clave.
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $Server
    $builder['Initial Catalog'] = $Database
    $builder['User ID'] = $UserId
    $builder['Password'] = $plain
    $builder['TrustServerCertificate'] = $true
    $connectionString = $builder.ConnectionString
}
finally {
    $plain = $null
}

# ---------- Probar el inicio de sesión ANTES de guardar ----------
if ($SkipConnectionTest) {
    Write-Warn 'Se omitió la prueba de inicio de sesión (-SkipConnectionTest): la clave no se comprobó.'
}
else {
    $test = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($connectionString)
    $test['Connect Timeout'] = 10
    $test['Pooling'] = $false
    $connection = New-Object System.Data.SqlClient.SqlConnection($test.ConnectionString)
    try {
        $connection.Open()
        Write-Ok "Inicio de sesión correcto como '$UserId' en $Server."
    }
    catch [System.Data.SqlClient.SqlException] {
        $number = $_.Exception.Number
        $connection.Dispose()
        throw ("No se pudo iniciar sesión (error $number): $(Describe-SqlError $number) No se cambió nada. " +
               'Si SQL Server aún no está disponible, repita con -SkipConnectionTest.')
    }
    finally {
        $connection.Dispose()
    }
}

# ---------- Escribir SOBRE EL MISMO archivo, conservando los permisos ----------
$config = [IO.File]::ReadAllText($fullPath) | ConvertFrom-Json
if ($null -eq $config) { $config = [pscustomobject]@{} }

$aclBefore = (Get-Acl -LiteralPath $fullPath).GetSecurityDescriptorSddlForm('Access')

$connectionStrings = Get-OrAddSection $config 'ConnectionStrings'
Set-ConfigValue $connectionStrings 'AspelSae' $connectionString
$api = Get-OrAddSection $config 'Api'
foreach ($name in 'BaseUrl', 'AgentKey') {
    if ($null -eq $api.PSObject.Properties[$name]) { Set-ConfigValue $api $name '' }
}
if ($PSBoundParameters.ContainsKey('BaseUrl')) { Set-ConfigValue $api 'BaseUrl' $BaseUrl.Trim().TrimEnd('/') }

[IO.File]::WriteAllText($fullPath, ($config | ConvertTo-Json -Depth 10), (New-Object System.Text.UTF8Encoding($false)))
$connectionString = $null

# Si por cualquier motivo los permisos cambiaron al escribir, se restauran los de antes.
$aclAfter = (Get-Acl -LiteralPath $fullPath).GetSecurityDescriptorSddlForm('Access')
if ($aclAfter -ne $aclBefore) {
    $acl = Get-Acl -LiteralPath $fullPath
    $acl.SetSecurityDescriptorSddlForm($aclBefore, 'Access')
    Set-Acl -LiteralPath $fullPath -AclObject $acl
    Write-Warn 'Los permisos del archivo cambiaron al escribirlo y se restauraron.'
}

Write-Ok "Cadena de conexión guardada en $fullPath (la contraseña no se muestra)."
if ($PSBoundParameters.ContainsKey('BaseUrl')) { Write-Ok "Api:BaseUrl guardada: $($BaseUrl.Trim().TrimEnd('/'))" }

$pendiente = @()
if ([string]::IsNullOrWhiteSpace([string]$api.BaseUrl) -or ([string]$api.BaseUrl) -match '[<>]') { $pendiente += 'Api:BaseUrl (repita con -BaseUrl https://<dominio>.up.railway.app)' }
if ([string]::IsNullOrWhiteSpace([string]$api.AgentKey) -or ([string]$api.AgentKey).Trim() -ieq 'CAMBIAR') { $pendiente += 'Api:AgentKey (New-SyncAgentKey.ps1 -ConfigPath ...)' }
if ($pendiente.Count -gt 0) {
    Write-Host ''
    Write-Host 'Todavía falta completar:'
    $pendiente | ForEach-Object { Write-Host "  - $_" }
}
