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

      -HashFromPrompt         Pide una llave existente en una línea oculta y muestra su hash (por ejemplo, para
                              comprobar qué hash corresponde a la llave instalada).

    ROTACIÓN SIN PERDER UN CICLO: Sync__AgentKeyHashes admite dos hashes separados por coma.
      1. En Railway: Sync__AgentKeyHashes = <hash anterior>,<hash nuevo>.
      2. En la tienda: .\New-SyncAgentKey.ps1 -ConfigPath ... (instala la llave nueva).
      3. Cuando el agente sincronice bien con la nueva: Sync__AgentKeyHashes = <hash nuevo>.

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
    Write-Host '  (para rotar sin cortes: "<hash anterior>,<hash nuevo>" y luego solo el nuevo)'
}

switch ($PSCmdlet.ParameterSetName) {
    'Store' {
        if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
            throw "No existe $ConfigPath. Instale primero el agente (Install-SyncAgent.ps1)."
        }

        $fullPath = (Resolve-Path -LiteralPath $ConfigPath).ProviderPath
        $config = [System.IO.File]::ReadAllText($fullPath) | ConvertFrom-Json

        $key = New-AgentKey
        try {
            if ($config.PSObject.Properties.Name -notcontains 'Api') {
                $config | Add-Member -NotePropertyName 'Api' -NotePropertyValue ([pscustomobject]@{ BaseUrl = ''; AgentKey = '' })
            }
            if ($config.Api.PSObject.Properties.Name -notcontains 'AgentKey') {
                $config.Api | Add-Member -NotePropertyName 'AgentKey' -NotePropertyValue ''
            }
            $config.Api.AgentKey = $key

            # Se reescribe el contenido del mismo archivo: conserva los permisos NTFS que puso el instalador.
            $json = $config | ConvertTo-Json -Depth 10
            [System.IO.File]::WriteAllText($fullPath, $json, (New-Object System.Text.UTF8Encoding($false)))
            $hash = Get-KeyHash $key
        }
        finally {
            $key = $null
        }

        Write-Host "Llave nueva guardada en $fullPath (Api.AgentKey). No se muestra." -ForegroundColor Green
        $baseUrlValue = if ($config.Api.PSObject.Properties.Name -contains 'BaseUrl') { $config.Api.BaseUrl } else { '' }
        if ([string]::IsNullOrWhiteSpace($baseUrlValue)) {
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
        & dotnet user-secrets set 'Sync:AgentKeyHashes' $hash --project $backendProject | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo guardar Sync:AgentKeyHashes en los User Secrets del backend.' }

        Write-Host 'Llave de desarrollo configurada:' -ForegroundColor Green
        Write-Host "  agente  : Api:AgentKey (no se muestra) y Api:BaseUrl = $BaseUrl"
        Write-Host "  backend : Sync:AgentKeyHashes = $hash"
        Write-Host 'Reinicie el backend para que lea el hash nuevo.'
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
