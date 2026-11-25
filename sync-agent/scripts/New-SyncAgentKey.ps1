<#
.SYNOPSIS
    Genera la llave del agente de sincronización y el hash que se configura en el backend (US-06).

.DESCRIPTION
    La llave es aleatoria: 32 bytes de RandomNumberGenerator, en base64url (43 caracteres). El backend NUNCA recibe
    la llave: solo su hash SHA-256 en hexadecimal (variable Sync__AgentKeyHashes en Railway). El hash no permite
    reconstruir la llave.

    La llave nunca se escribe como argumento de un comando, así que no queda en el historial de PowerShell
    (ConsoleHost_history.txt). No use Start-Transcript mientras ejecuta este script.

    Hay tres modos (use exactamente uno):

      -ConfigPath <archivo>   EN LA TIENDA, en una consola de administrador. Genera una llave nueva, la escribe en
                              Api.AgentKey de appsettings.Production.json (el archivo conserva sus permisos) y muestra
                              SOLO el hash, para pegarlo en Railway.

      -Development            EN DESARROLLO. Genera una llave de desarrollo, la guarda en los User Secrets del agente
                              (Api:AgentKey, más Api:BaseUrl) y el hash en los del backend (Sync:AgentKeyHashes).
                              Usa "dotnet user-secrets", que recibe los valores como argumentos: es aceptable para una
                              llave de desarrollo, nunca para la de la tienda.
                              ADVERTENCIA: REEMPLAZA Sync:AgentKeyHashes en los secretos del backend. Cualquier otra
                              instalación local con una llave distinta (por ejemplo la tarea programada instalada con
                              -ConfigPath) recibirá 401 hasta que su hash se agregue. Se admiten dos hashes separados por coma:
                              dotnet user-secrets set "Sync:AgentKeyHashes" "<hash 1>,<hash 2>" --project backend\src\backend

      -HashFromPrompt         Pide una llave existente en una línea oculta y muestra su hash (por ejemplo, para
                              comprobar qué hash corresponde a la llave instalada).

    ROTACIÓN: Sync__AgentKeyHashes admite dos hashes separados por coma. El script escribe la llave nueva ANTES de que
    Railway conozca su hash, así que durante el cambio se deshabilita la tarea para que el agente no falle en medio.
      1. Disable-ScheduledTask -TaskPath '\NettesSyncAgent\' -TaskName 'Sincronizacion'
      2. .\New-SyncAgentKey.ps1 -ConfigPath ...   (instala la llave nueva y muestra su hash)
      3. En Railway: Sync__AgentKeyHashes = <hash anterior>,<hash nuevo>.
      4. SyncAgent.exe --test; si dice que la llave es válida: Enable-ScheduledTask ...
      5. Cuando el agente sincronice bien con la nueva: Sync__AgentKeyHashes = <hash nuevo>.

.PARAMETER ConfigPath
    Ruta de appsettings.Production.json del agente instalado. Por ejemplo: C:\NettesSyncAgent\appsettings.Production.json

.PARAMETER Development
    Configura la llave en los User Secrets del agente y del backend de este repositorio.

.PARAMETER BaseUrl
    Solo con -Development: URL de la API para el agente. Por omisión http://localhost:3000 (el frontend, que reenvía
    /api/* al backend, como en Railway).

.PARAMETER HashFromPrompt
    Pide una llave y muestra su hash.

.EXAMPLE
    .\New-SyncAgentKey.ps1 -ConfigPath C:\NettesSyncAgent\appsettings.Production.json

.EXAMPLE
    .\New-SyncAgentKey.ps1 -Development
#>
[CmdletBinding(DefaultParameterSetName = 'Store')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Store')]
    [string] $ConfigPath,

    [Parameter(Mandatory = $true, ParameterSetName = 'Development')]
    [switch] $Development,

    [Parameter(ParameterSetName = 'Development')]
    [string] $BaseUrl = 'http://localhost:3000',

    [Parameter(Mandatory = $true, ParameterSetName = 'Hash')]
    [switch] $HashFromPrompt
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-AgentKey {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    # base64url sin relleno: solo letras, números, "-" y "_" (no hay que escapar nada en JSON ni en encabezados).
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Get-KeyHash([string] $key) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($key)) } finally { $sha.Dispose() }
    return -join ($hash | ForEach-Object { $_.ToString('x2') })
}

function Write-HashInstructions([string] $hash) {
    Write-Host ''
    Write-Host 'Hash para el backend (no es la llave; se puede copiar sin riesgo):' -ForegroundColor Cyan
    Write-Host "  $hash"
    Write-Host ''
    Write-Host 'En Railway, servicio backend, Variables:'
    Write-Host "  Sync__AgentKeyHashes = $hash"
    Write-Host '  (para rotar: deshabilite la tarea, "<hash anterior>,<hash nuevo>" en Railway, habilite la tarea y luego solo el nuevo)'
}

switch ($PSCmdlet.ParameterSetName) {
    'Store' {
        if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
            throw "No existe $ConfigPath. Instale primero el agente (Install-SyncAgent.ps1)."
        }

        $fullPath = (Resolve-Path -LiteralPath $ConfigPath).ProviderPath
        $config = [System.IO.File]::ReadAllText($fullPath) | ConvertFrom-Json
        if ($null -eq $config) { $config = [pscustomobject]@{} }

        # Permisos del archivo ANTES de tocarlo (solo la lista de accesos): se comparan al final.
        $aclBefore = (Get-Acl -LiteralPath $fullPath).GetSecurityDescriptorSddlForm('Access')

        $key = New-AgentKey
        try {
            # Acceso seguro bajo Set-StrictMode: la sección Api puede faltar, ser nula o no tener alguna clave.
            $api = $null
            if ($null -ne $config.PSObject.Properties['Api']) { $api = $config.PSObject.Properties['Api'].Value }
            if ($api -isnot [System.Management.Automation.PSCustomObject]) {
                $api = [pscustomobject]@{}
                $config | Add-Member -NotePropertyName 'Api' -NotePropertyValue $api -Force
            }
            foreach ($name in 'BaseUrl', 'AgentKey') {
                if ($null -eq $api.PSObject.Properties[$name]) { $api | Add-Member -NotePropertyName $name -NotePropertyValue '' }
            }
            $api.AgentKey = $key
            $baseUrlValue = [string]$api.BaseUrl

            # Se escribe SOBRE EL MISMO ARCHIVO (no un temporal que luego se mueve): un archivo nuevo heredaría los permisos de la
            # carpeta y quedaría legible para más cuentas. Así conserva la lista de permisos que puso el instalador.
            $json = $config | ConvertTo-Json -Depth 10
            [System.IO.File]::WriteAllText($fullPath, $json, (New-Object System.Text.UTF8Encoding($false)))
            $hash = Get-KeyHash $key
        }
        finally {
            $key = $null
        }

        # Comprobación: si por cualquier motivo los permisos cambiaron, se restauran los de antes.
        $aclAfter = (Get-Acl -LiteralPath $fullPath).GetSecurityDescriptorSddlForm('Access')
        if ($aclAfter -ne $aclBefore) {
            $acl = Get-Acl -LiteralPath $fullPath
            $acl.SetSecurityDescriptorSddlForm($aclBefore, 'Access')
            Set-Acl -LiteralPath $fullPath -AclObject $acl
            Write-Host 'AVISO: los permisos del archivo cambiaron al escribirlo y se restauraron.' -ForegroundColor Yellow
        }

        Write-Host "Llave nueva guardada en $fullPath (Api.AgentKey). No se muestra." -ForegroundColor Green
        if ([string]::IsNullOrWhiteSpace($baseUrlValue) -or $baseUrlValue -match '[<>]') {
            Write-Host 'Falta Api.BaseUrl en ese archivo: escriba la URL pública del sitio (https://<dominio>.up.railway.app).' -ForegroundColor Yellow
        }
        Write-HashInstructions $hash
        Write-Host ''
        Write-Host 'Después de configurar Railway, compruebe con:  SyncAgent.exe --test'
    }

    'Development' {
        $agentProject = Join-Path $PSScriptRoot '..\src\SyncAgent'
        $backendProject = Join-Path $PSScriptRoot '..\..\backend\src\backend'
        foreach ($project in @($agentProject, $backendProject)) {
            if (-not (Test-Path -LiteralPath $project)) { throw "No se encontró el proyecto $project." }
        }

        $key = New-AgentKey
        try {
            $hash = Get-KeyHash $key
            & dotnet user-secrets set 'Api:AgentKey' $key --project $agentProject | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'No se pudo guardar Api:AgentKey en los User Secrets del agente.' }
        }
        finally {
            $key = $null
        }

        & dotnet user-secrets set 'Api:BaseUrl' $BaseUrl --project $agentProject | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo guardar Api:BaseUrl en los User Secrets del agente.' }

        # ¿Ya había un hash? (solo se mira si la clave existe; el valor no se muestra)
        $hadHash = $null -ne (& dotnet user-secrets list --project $backendProject 2>$null | Where-Object { $_ -like 'Sync:AgentKeyHashes*' })
        & dotnet user-secrets set 'Sync:AgentKeyHashes' $hash --project $backendProject | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo guardar Sync:AgentKeyHashes en los User Secrets del backend.' }

        Write-Host 'Llave de desarrollo configurada:' -ForegroundColor Green
        Write-Host "  agente  : Api:AgentKey (no se muestra) y Api:BaseUrl = $BaseUrl"
        Write-Host "  backend : Sync:AgentKeyHashes = $hash"
        Write-Host 'Reinicie el backend para que lea el hash nuevo.'
        Write-Host ''
        if ($hadHash) {
            Write-Host 'AVISO: esto REEMPLAZÓ el Sync:AgentKeyHashes que ya tenía el backend.' -ForegroundColor Yellow
        }
        else {
            Write-Host 'AVISO: esto guardó Sync:AgentKeyHashes en los secretos del backend; si luego se vuelve a ejecutar, lo REEMPLAZA.' -ForegroundColor Yellow
        }
        Write-Host '       Las instalaciones locales con otra llave (por ejemplo la tarea programada) dejarán de funcionar (401).' -ForegroundColor Yellow
        Write-Host '       Para aceptar dos llaves a la vez (máximo 2, separadas por coma):' -ForegroundColor Yellow
        Write-Host '         dotnet user-secrets set "Sync:AgentKeyHashes" "<hash 1>,<hash 2>" --project backend\src\backend' -ForegroundColor Yellow
    }

    'Hash' {
        $secure = Read-Host -AsSecureString 'Llave del agente (no se ve al escribir)'
        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try {
            $key = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
            if ([string]::IsNullOrEmpty($key)) { throw 'No se escribió ninguna llave.' }
            $hash = Get-KeyHash $key
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
            $key = $null
        }

        Write-HashInstructions $hash
    }
}
