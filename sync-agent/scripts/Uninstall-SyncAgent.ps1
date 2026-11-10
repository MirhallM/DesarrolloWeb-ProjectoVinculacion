<#
.SYNOPSIS
    Desinstala el agente de sincronización: deshace todo lo que hizo Install-SyncAgent.ps1.

.DESCRIPTION
    Qué hace, en 5 pasos (cada uno se muestra en pantalla mientras corre):

      1. Verifica que se ejecuta como administrador.
      2. Detiene y elimina la tarea programada y su carpeta del Programador de tareas.
      3. Espera a que termine (o cierra) cualquier ejecución del agente que siga en curso.
      4. Quita a la cuenta local del agente el derecho de inicio de sesión por lotes, la elimina y borra su perfil de usuario.
      5. Elimina la carpeta de instalación (o, con -KeepData, solo el programa) y los permisos que quedaron.

    Al final comprueba que la tarea, la cuenta y la carpeta ya no existen y lo informa.

    Se puede ejecutar varias veces: lo que ya no exista se informa como "ya no existía" y NO es un error.

    NO toca SQL Server: la cuenta 'catalogo_lectura' de la base se quita con el bloque comentado al final de
    sql\install\01-crear-login-lectura.sql.

.PARAMETER InstallPath
    Carpeta donde se instaló el agente. Por omisión: C:\NettesSyncAgent

.PARAMETER AccountName
    Cuenta local que ejecutaba la tarea. Por omisión: svc-nettes-sync

.PARAMETER KeepData
    Conserva appsettings.Production.json y la carpeta data\ (estado y logs); elimina todo lo demás. Sirve para
    reinstalar después sin volver a escribir la configuración. Los permisos de la cuenta eliminada también se quitan.

.PARAMETER Force
    Elimina la cuenta aunque no la haya creado el instalador, y la carpeta aunque no parezca una instalación del agente.
    Úselo solo si está seguro.

.EXAMPLE
    .\Uninstall-SyncAgent.ps1

.EXAMPLE
    .\Uninstall-SyncAgent.ps1 -InstallPath D:\NettesSyncAgent -KeepData
#>
# (Esta línea va DESPUÉS del bloque de ayuda a propósito: si va antes, PowerShell 5.1 no reconoce la ayuda de Get-Help.)
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$InstallPath = 'C:\NettesSyncAgent',
    [ValidatePattern('^[A-Za-z0-9._-]{1,20}$')]
    [string]$AccountName = 'svc-nettes-sync',
    [switch]$KeepData,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------------------------------
# Constantes (las mismas que usa Install-SyncAgent.ps1)
# ---------------------------------------------------------------------------------------------------------------------
$TaskName = 'Sincronizacion'
$TaskFolder = '\NettesSyncAgent\'
$TaskFolderName = 'NettesSyncAgent'
$AccountMarker = 'NettesSyncAgent: cuenta del agente'
$TotalSteps = 5

function Write-Step([int]$Number, [string]$Text) {
    Write-Host ''
    Write-Host "[$Number/$TotalSteps] $Text" -ForegroundColor Cyan
}
function Write-Info([string]$Text) { Write-Host "      $Text" }
function Write-Ok([string]$Text) { Write-Host "      OK     $Text" -ForegroundColor Green }
function Write-Warn([string]$Text) { Write-Host "      AVISO  $Text" -ForegroundColor Yellow }

function Get-DeadIdentities([string]$Path, [string]$Sid) {
    # Identidades de la lista de permisos de $Path que son la cuenta eliminada ($Sid) o que ya no corresponden a ninguna
    # cuenta (Windows las muestra como un identificador "S-1-5-21-..."). Las cuentas que existen (Administradores, SYSTEM)
    # se muestran con su nombre y nunca se tocan.
    $result = New-Object System.Collections.Generic.List[object]
    $seen = @{}
    # Solo se lee la sección de permisos (Access): leer también la de auditoría exige un privilegio que no hace falta.
    $item = Get-Item -LiteralPath $Path -Force
    foreach ($rule in $item.GetAccessControl([System.Security.AccessControl.AccessControlSections]::Access).Access) {
        $identity = $rule.IdentityReference
        $value = [string]$identity.Value
        $asSid = $value
        try { $asSid = $identity.Translate([System.Security.Principal.SecurityIdentifier]).Value } catch { }
        if ((($Sid) -and ($asSid -eq $Sid)) -or ($value -match '^S-1-\d+(-\d+)+$')) {
            if (-not $seen.ContainsKey($value)) { $seen[$value] = $true; $result.Add($identity) }
        }
    }
    # Se devuelven los elementos sueltos (no la lista envuelta en otra): quien llama los recoge con @( ).
    return $result.ToArray()
}

function Remove-DeadIdentities([string]$Path, [string]$Sid) {
    # Quita esos permisos. Se usa la API de permisos de .NET y NO icacls: icacls no puede quitar el permiso de una cuenta que
    # ya no existe (termina con el error 1332, "no hay asignación entre nombres de cuenta e identificadores").
    $dead = @(Get-DeadIdentities -Path $Path -Sid $Sid)
    if ($dead.Count -eq 0) { return 0 }
    # Solo se escribe la sección de permisos. Set-Acl reescribiría también propietario y auditoría, y eso pide el privilegio
    # SeSecurityPrivilege, que no hace falta para quitar un permiso.
    $item = Get-Item -LiteralPath $Path -Force
    $acl = $item.GetAccessControl([System.Security.AccessControl.AccessControlSections]::Access)
    foreach ($identity in $dead) { $acl.PurgeAccessRules($identity) }
    $item.SetAccessControl($acl)
    return $dead.Count
}

function Get-AgentProcesses([string]$Folder) {
    return @(Get-Process -Name 'SyncAgent' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -like "$Folder\*" })
}

# ---------------------------------------------------------------------------------------------------------------------
# Derechos de inicio de sesión de Windows (API LSA)
# ---------------------------------------------------------------------------------------------------------------------
# PowerShell no trae un comando para conceder derechos como "Iniciar sesión como trabajo por lotes". Se usa la API LSA de
# Windows (LsaAddAccountRights / LsaRemoveAccountRights) mediante un trozo pequeño de C# que PowerShell compila al vuelo.
# Agrega o quita UN derecho a UNA cuenta, sin tocar a nadie más. Este mismo bloque está en Install-SyncAgent.ps1 y en
# Uninstall-SyncAgent.ps1 (cada script es independiente).
if (-not ('NettesLsaRights' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

public static class NettesLsaRights
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_OBJECT_ATTRIBUTES
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("advapi32.dll")]
    private static extern uint LsaOpenPolicy(IntPtr systemName, ref LSA_OBJECT_ATTRIBUTES attributes, uint desiredAccess, out IntPtr policyHandle);
    [DllImport("advapi32.dll")]
    private static extern uint LsaAddAccountRights(IntPtr policyHandle, IntPtr accountSid, LSA_UNICODE_STRING[] userRights, uint countOfRights);
    [DllImport("advapi32.dll")]
    private static extern uint LsaRemoveAccountRights(IntPtr policyHandle, IntPtr accountSid, [MarshalAs(UnmanagedType.U1)] bool allRights, LSA_UNICODE_STRING[] userRights, uint countOfRights);
    [DllImport("advapi32.dll")]
    private static extern uint LsaEnumerateAccountRights(IntPtr policyHandle, IntPtr accountSid, out IntPtr userRights, out uint countOfRights);
    [DllImport("advapi32.dll")]
    private static extern uint LsaFreeMemory(IntPtr buffer);
    [DllImport("advapi32.dll")]
    private static extern uint LsaClose(IntPtr policyHandle);
    [DllImport("advapi32.dll")]
    private static extern int LsaNtStatusToWinError(uint status);

    private const uint POLICY_CREATE_ACCOUNT = 0x00000010;
    private const uint POLICY_LOOKUP_NAMES = 0x00000800;
    private const uint STATUS_OBJECT_NAME_NOT_FOUND = 0xC0000034;
    private const uint STATUS_NO_SUCH_PRIVILEGE = 0xC0000060;

    private static void Check(uint status)
    {
        if (status != 0) throw new Win32Exception(LsaNtStatusToWinError(status));
    }

    private static IntPtr OpenPolicy(uint access)
    {
        LSA_OBJECT_ATTRIBUTES attributes = new LSA_OBJECT_ATTRIBUTES();
        attributes.Length = Marshal.SizeOf(typeof(LSA_OBJECT_ATTRIBUTES));
        IntPtr policy;
        Check(LsaOpenPolicy(IntPtr.Zero, ref attributes, access, out policy));
        return policy;
    }

    private static IntPtr AllocSid(string sidText)
    {
        SecurityIdentifier sid = new SecurityIdentifier(sidText);
        byte[] bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        IntPtr pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }

    private static LSA_UNICODE_STRING MakeString(string text, out IntPtr memory)
    {
        memory = Marshal.StringToHGlobalUni(text);
        LSA_UNICODE_STRING value = new LSA_UNICODE_STRING();
        value.Length = (ushort)(text.Length * 2);
        value.MaximumLength = (ushort)((text.Length + 1) * 2);
        value.Buffer = memory;
        return value;
    }

    /// <summary>Concede UN derecho (p. ej. SeBatchLogonRight) a UNA cuenta. Si ya lo tiene, no hace nada.</summary>
    public static void Add(string sid, string right)
    {
        IntPtr policy = OpenPolicy(POLICY_CREATE_ACCOUNT | POLICY_LOOKUP_NAMES);
        IntPtr sidPointer = AllocSid(sid);
        IntPtr textMemory;
        LSA_UNICODE_STRING[] rights = new LSA_UNICODE_STRING[] { MakeString(right, out textMemory) };
        try { Check(LsaAddAccountRights(policy, sidPointer, rights, 1)); }
        finally { Marshal.FreeHGlobal(textMemory); Marshal.FreeHGlobal(sidPointer); LsaClose(policy); }
    }

    /// <summary>Quita UN derecho a UNA cuenta (funciona aunque la cuenta ya se haya eliminado). Si no lo tenía, no hace nada.</summary>
    public static void Remove(string sid, string right)
    {
        IntPtr policy = OpenPolicy(POLICY_CREATE_ACCOUNT | POLICY_LOOKUP_NAMES);
        IntPtr sidPointer = AllocSid(sid);
        IntPtr textMemory;
        LSA_UNICODE_STRING[] rights = new LSA_UNICODE_STRING[] { MakeString(right, out textMemory) };
        try
        {
            uint status = LsaRemoveAccountRights(policy, sidPointer, false, rights, 1);
            if (status != STATUS_OBJECT_NAME_NOT_FOUND && status != STATUS_NO_SUCH_PRIVILEGE) Check(status);
        }
        finally { Marshal.FreeHGlobal(textMemory); Marshal.FreeHGlobal(sidPointer); LsaClose(policy); }
    }

    /// <summary>¿La cuenta tiene este derecho asignado directamente? (solo lectura)</summary>
    public static bool Has(string sid, string right)
    {
        IntPtr policy = OpenPolicy(POLICY_LOOKUP_NAMES);
        IntPtr sidPointer = AllocSid(sid);
        try
        {
            IntPtr list; uint count;
            uint status = LsaEnumerateAccountRights(policy, sidPointer, out list, out count);
            if (status == STATUS_OBJECT_NAME_NOT_FOUND) return false; // la cuenta no tiene ningún derecho asignado
            Check(status);
            try
            {
                int size = Marshal.SizeOf(typeof(LSA_UNICODE_STRING));
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = new IntPtr(list.ToInt64() + (long)i * size);
                    LSA_UNICODE_STRING entry = (LSA_UNICODE_STRING)Marshal.PtrToStructure(item, typeof(LSA_UNICODE_STRING));
                    string name = Marshal.PtrToStringUni(entry.Buffer, entry.Length / 2);
                    if (string.Equals(name, right, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            finally { LsaFreeMemory(list); }
        }
        finally { Marshal.FreeHGlobal(sidPointer); LsaClose(policy); }
    }
}
'@
}

try {
    # =================================================================================================================
    Write-Step 1 'Verificar requisitos'
    # =================================================================================================================
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $isAdmin = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        throw ('Este script debe ejecutarse en una consola de PowerShell abierta COMO ADMINISTRADOR ' +
               '(clic derecho en PowerShell > "Ejecutar como administrador").')
    }
    Write-Ok "Consola de administrador ($($identity.Name))."

    $InstallPath = [IO.Path]::GetFullPath($InstallPath).TrimEnd('\')
    if ($InstallPath.Length -le 3) {
        throw "La carpeta de instalación no puede ser la raíz de un disco ($InstallPath)."
    }

    # SID de la cuenta, anotado ANTES de borrarla: sirve para quitar sus permisos y su perfil después.
    $account = Get-LocalUser -Name $AccountName -ErrorAction SilentlyContinue
    $accountSid = $null
    if ($null -ne $account) { $accountSid = $account.SID.Value }

    # =================================================================================================================
    Write-Step 2 "Eliminar la tarea programada $TaskFolder$TaskName"
    # =================================================================================================================
    $task = Get-ScheduledTask -TaskName $TaskName -TaskPath $TaskFolder -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        Write-Info 'La tarea ya no existía.'
    }
    else {
        if ($task.State -eq 'Running') {
            Stop-ScheduledTask -TaskName $TaskName -TaskPath $TaskFolder
            Write-Info 'Se detuvo la ejecución en curso.'
        }
        Unregister-ScheduledTask -TaskName $TaskName -TaskPath $TaskFolder -Confirm:$false
        Write-Ok 'Tarea eliminada.'
    }

    # Los cmdlets no pueden borrar la CARPETA del Programador de tareas; se usa el servicio COM del propio Windows.
    try {
        $scheduler = New-Object -ComObject 'Schedule.Service'
        $scheduler.Connect()
        $rootFolder = $scheduler.GetFolder('\')
        $agentFolder = $null
        try { $agentFolder = $rootFolder.GetFolder($TaskFolderName) } catch { $agentFolder = $null }

        if ($null -eq $agentFolder) {
            Write-Info 'La carpeta del Programador de tareas ya no existía.'
        }
        elseif ($agentFolder.GetTasks(0).Count -gt 0) {
            Write-Warn "La carpeta $TaskFolder contiene otras tareas ajenas al agente: no se elimina."
        }
        else {
            $rootFolder.DeleteFolder($TaskFolderName, 0)
            Write-Ok "Carpeta $TaskFolder del Programador de tareas eliminada."
        }
    }
    catch {
        Write-Warn "No se pudo eliminar la carpeta del Programador de tareas: $($_.Exception.Message)"
    }

    # =================================================================================================================
    Write-Step 3 'Esperar a que no quede ninguna ejecución del agente'
    # =================================================================================================================
    # OJO: el resultado se envuelve en @( ). Una función que devuelve una lista vacía entrega $null a quien la llama, y con
    # Set-StrictMode pedir .Count a un $null es un error.
    $processes = @(Get-AgentProcesses -Folder $InstallPath)
    if ($processes.Count -eq 0) {
        Write-Info 'No hay ninguna ejecución del agente en curso.'
    }
    else {
        Write-Info 'Hay una ejecución en curso; esperando a que termine (máximo 30 s)...'
        $processes | ForEach-Object { [void]$_.WaitForExit(30000) }
        $still = @(Get-AgentProcesses -Folder $InstallPath)
        if ($still.Count -gt 0) {
            Write-Warn 'Sigue en ejecución: se cierra a la fuerza (el agente solo lee y guarda su estado; es seguro).'
            $still | Stop-Process -Force
            $still | ForEach-Object { [void]$_.WaitForExit(10000) }
        }
        Write-Ok 'Ninguna ejecución del agente en curso.'
    }

    # Cualquier otro proceso que siga corriendo CON LA CUENTA DEDICADA (por ejemplo el anfitrión de una tarea) mantiene
    # cargado su perfil y impediría borrarlo. Solo se tocan procesos de esa cuenta; nunca los de otros usuarios.
    $ownProcesses = @(Get-Process -IncludeUserName -ErrorAction SilentlyContinue | Where-Object { $_.UserName -like "*\$AccountName" })
    if ($ownProcesses.Count -gt 0) {
        Write-Warn "Hay $($ownProcesses.Count) proceso(s) ejecutándose con la cuenta '$AccountName': $((($ownProcesses | ForEach-Object { $_.ProcessName }) | Sort-Object -Unique) -join ', '). Se cierran."
        $ownProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }

    # =================================================================================================================
    Write-Step 4 "Eliminar la cuenta local '$AccountName' y su perfil"
    # =================================================================================================================
    if ($null -eq $account) {
        Write-Info 'La cuenta ya no existía.'
    }
    elseif (($account.Description -ne $AccountMarker) -and -not $Force) {
        Write-Warn "La cuenta '$AccountName' NO fue creada por el instalador del agente (su descripción es distinta): no se elimina."
        Write-Warn 'Si está seguro de que es la cuenta del agente, vuelva a ejecutar con -Force.'
    }
    else {
        # Antes de borrar la cuenta se le quita el derecho de inicio de sesión por lotes que concedió el instalador;
        # si no, quedaría un identificador huérfano en la directiva de seguridad del equipo.
        if ($null -ne $accountSid) {
            [NettesLsaRights]::Remove($accountSid, 'SeBatchLogonRight')
            Write-Ok "Derecho 'Iniciar sesión como trabajo por lotes' quitado a '$AccountName'."
        }
        Remove-LocalUser -Name $AccountName
        Write-Ok "Cuenta '$AccountName' eliminada."
    }

    # El perfil de usuario (C:\Users\<cuenta>) lo crea Windows la primera vez que la tarea corre con esa cuenta y
    # NO se borra al eliminar la cuenta. Se busca por SID y también por nombre de carpeta (por si la cuenta ya no existía).
    $cuentaEliminada = ($null -eq (Get-LocalUser -Name $AccountName -ErrorAction SilentlyContinue))
    $profiles = @()
    if ($cuentaEliminada) {
        $profiles = @(Get-CimInstance -ClassName Win32_UserProfile -ErrorAction SilentlyContinue |
            Where-Object { (($null -ne $accountSid) -and ($_.SID -eq $accountSid)) -or ($_.LocalPath -like "*\$AccountName") })
    }
    foreach ($userProfile in $profiles) {
        # Aunque la tarea ya no exista, Windows puede tardar un poco en descargar el perfil (hasta que se cierre la última
        # sesión de la cuenta). Se espera hasta 60 s a que lo suelte.
        $waited = 0
        while ($userProfile -and $userProfile.Loaded -and $waited -lt 60) {
            if ($waited -eq 0) { Write-Info 'El perfil de la cuenta sigue cargado; esperando a que Windows lo descargue (máximo 60 s)...' }
            Start-Sleep -Seconds 5
            $waited += 5
            $userProfile = Get-CimInstance -ClassName Win32_UserProfile -Filter "SID='$($userProfile.SID)'" -ErrorAction SilentlyContinue
        }
        if (-not $userProfile) { Write-Ok 'El perfil ya había desaparecido.'; continue }
        if ($userProfile.Loaded) {
            Write-Warn "El perfil $($userProfile.LocalPath) sigue en uso por Windows y no se pudo eliminar ahora."
            Write-Warn 'Se elimina al reiniciar el equipo y volver a ejecutar este script (es seguro repetirlo).'
            continue
        }
        $userProfile | Remove-CimInstance
        Write-Ok "Perfil eliminado: $($userProfile.LocalPath)"
    }
    if ($cuentaEliminada -and $profiles.Count -eq 0) { Write-Info 'No había perfil de usuario que eliminar.' }

    # =================================================================================================================
    Write-Step 5 "Eliminar los archivos de $InstallPath"
    # =================================================================================================================
    if (-not (Test-Path -LiteralPath $InstallPath)) {
        Write-Info 'La carpeta de instalación ya no existía.'
    }
    else {
        $looksLikeAgent = (Test-Path -LiteralPath (Join-Path $InstallPath 'SyncAgent.exe')) -or
                          (Test-Path -LiteralPath (Join-Path $InstallPath 'appsettings.Production.example.json')) -or
                          (Test-Path -LiteralPath (Join-Path $InstallPath 'data'))
        if (-not $looksLikeAgent -and -not $Force) {
            throw "La carpeta $InstallPath no parece una instalación del agente (no tiene SyncAgent.exe ni data\). No se borra; use -Force si está seguro."
        }

        if ($KeepData) {
            $keep = @('data', 'appsettings.Production.json')
            Get-ChildItem -LiteralPath $InstallPath -Force |
                Where-Object { $keep -notcontains $_.Name } |
                Remove-Item -Recurse -Force
            Write-Ok 'Programa eliminado. Se conservaron: appsettings.Production.json y data\.'

            # Lo conservado tenía permisos para la cuenta eliminada; ahora serían identificadores huérfanos. Se quitan (primero la
            # carpeta raíz, para que el cambio se propague a lo de adentro, y luego lo que tiene permisos propios). Funciona
            # también si la cuenta ya se había eliminado en una ejecución anterior: se quitan los identificadores huérfanos.
            $removed = 0
            foreach ($target in @($InstallPath, (Join-Path $InstallPath 'data'), (Join-Path $InstallPath 'appsettings.Production.json'))) {
                if (Test-Path -LiteralPath $target) { $removed += Remove-DeadIdentities -Path $target -Sid $accountSid }
            }
            Write-Ok "Permisos de la cuenta eliminada quitados de lo conservado ($removed identificador(es))."
        }
        else {
            Remove-Item -LiteralPath $InstallPath -Recurse -Force
            Write-Ok 'Carpeta de instalación eliminada (incluye configuración, estado y logs).'
        }
    }

    # =================================================================================================================
    Write-Host ''
    Write-Host 'Comprobación final' -ForegroundColor Cyan
    # =================================================================================================================
    $problems = 0
    function Write-Check([string]$What, [bool]$Gone, [string]$IfNot) {
        if ($Gone) { Write-Host "      OK     $What" -ForegroundColor Green }
        else { Write-Host "      PROBLEMA  $What  ->  $IfNot" -ForegroundColor Red; $script:problems++ }
    }

    $taskGone = ($null -eq (Get-ScheduledTask -TaskName $TaskName -TaskPath $TaskFolder -ErrorAction SilentlyContinue))
    Write-Check "La tarea $TaskFolder$TaskName ya no existe" $taskGone 'sigue registrada'

    $folderGone = $true
    try { $s = New-Object -ComObject 'Schedule.Service'; $s.Connect(); $null = $s.GetFolder($TaskFolderName); $folderGone = $false } catch { $folderGone = $true }
    if ($folderGone) { Write-Check "La carpeta $TaskFolder del Programador de tareas ya no existe" $true '' }
    else { Write-Host "      AVISO  La carpeta $TaskFolder del Programador de tareas sigue existiendo (puede contener otras tareas)." -ForegroundColor Yellow }

    $accountGone = ($null -eq (Get-LocalUser -Name $AccountName -ErrorAction SilentlyContinue))
    if ($accountGone) { Write-Check "La cuenta '$AccountName' ya no existe" $true '' }
    elseif (($account -ne $null) -and ($account.Description -ne $AccountMarker) -and -not $Force) {
        Write-Host "      AVISO  La cuenta '$AccountName' se conservó a propósito (no la creó el instalador)." -ForegroundColor Yellow
    }
    else { Write-Check "La cuenta '$AccountName' ya no existe" $false 'sigue existiendo' }

    # El derecho concedido por el instalador no debe quedar como identificador huérfano en la directiva del equipo.
    if ($accountGone -and ($null -ne $accountSid)) {
        $rightLeft = [NettesLsaRights]::Has($accountSid, 'SeBatchLogonRight')
        Write-Check 'El derecho de inicio de sesión por lotes de la cuenta ya no figura en la directiva del equipo' (-not $rightLeft) 'queda un identificador huérfano'
    }

    if ($KeepData) {
        Write-Check "El programa ya no está en $InstallPath (solo data\ y la configuración)" (-not (Test-Path -LiteralPath (Join-Path $InstallPath 'SyncAgent.exe'))) 'SyncAgent.exe sigue ahí'
        $deadLeft = 0
        foreach ($target in @($InstallPath, (Join-Path $InstallPath 'data'), (Join-Path $InstallPath 'appsettings.Production.json'))) {
            if (Test-Path -LiteralPath $target) { $deadLeft += @(Get-DeadIdentities -Path $target -Sid $accountSid).Count }
        }
        Write-Check 'Los permisos de la cuenta eliminada ya no aparecen en lo conservado (carpeta, data\ y configuración)' ($deadLeft -eq 0) 'queda un permiso huérfano'
    }
    else {
        Write-Check "La carpeta $InstallPath ya no existe" (-not (Test-Path -LiteralPath $InstallPath)) 'sigue existiendo'
    }

    $profilesLeft = @(Get-CimInstance -ClassName Win32_UserProfile -ErrorAction SilentlyContinue |
        Where-Object { (($null -ne $accountSid) -and ($_.SID -eq $accountSid)) -or ($_.LocalPath -like "*\$AccountName") })
    if (@($profilesLeft | Where-Object { $_.Loaded }).Count -gt 0) {
        # Windows aún lo tiene cargado: no es un fallo del script, pero se informa claro y no se da por "todo limpio".
        Write-Host "      AVISO  El perfil de usuario de '$AccountName' sigue cargado por Windows. Reinicie el equipo y vuelva a ejecutar este script para eliminarlo." -ForegroundColor Yellow
    }
    else {
        Write-Check "El perfil de usuario de '$AccountName' ya no existe" ($profilesLeft.Count -eq 0) 'queda un perfil en C:\Users'
    }

    Write-Host ''
    if ($problems -gt 0) {
        Write-Host "Desinstalación terminada CON $problems problema(s); revise los mensajes anteriores." -ForegroundColor Red
        exit 1
    }
    Write-Host 'Desinstalación terminada.' -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ''
    Write-Host 'ERROR: la desinstalación no se completó.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host 'El script se puede volver a ejecutar: lo que ya no exista se informa y no es un error.' -ForegroundColor Yellow
    exit 1
}
