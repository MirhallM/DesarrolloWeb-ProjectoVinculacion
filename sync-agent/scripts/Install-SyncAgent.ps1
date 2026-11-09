<#
.SYNOPSIS
    Instala (o actualiza) el agente de sincronización de Mini Super Nettes como una TAREA PROGRAMADA de Windows.

.DESCRIPTION
    Qué hace, en 7 pasos (cada uno se muestra en pantalla mientras corre):

      1. Verifica requisitos (consola de administrador, carpeta publicada, módulos de Windows).
      2. Crea (o actualiza) una cuenta local DEDICADA, sin privilegios de administrador, que ejecutará la tarea, y le
         concede el único derecho especial que necesita: iniciar sesión "como trabajo por lotes" (sin él la tarea no arranca).
      3. Copia el programa a la carpeta de instalación, SIN tocar la configuración ni los datos que ya existan.
      4. Aplica permisos NTFS: la cuenta puede LEER el programa y ESCRIBIR solo en la carpeta data\.
      5. Prepara appsettings.Production.json (la configuración con la cadena de conexión) y lo protege.
      6. Registra la tarea programada con las opciones descritas más abajo.
      7. Muestra un resumen y cómo verificar todo a mano.

    Se puede ejecutar varias veces: si la cuenta, los archivos o la tarea ya existen, los ACTUALIZA en lugar de fallar.
    Para deshacerlo todo: Uninstall-SyncAgent.ps1.

    OPCIONES DE LA TAREA PROGRAMADA (todas se definen en el paso 6):
      - Se ejecuta aunque NO haya sesión iniciada ............ LogonType "Password": la cuenta se autentica con su contraseña.
      - Con una cuenta dedicada, NO administradora ............ RunLevel "LeastPrivilege": sin elevación de privilegios.
      - Primer disparo pocos minutos después de ENCENDER ...... BootTrigger con retraso (3 min por omisión).
      - Luego cada 15 minutos, SIN FIN ........................ TimeTrigger con repetición y sin duración (= indefinida).
      - Si la anterior sigue corriendo, NO inicia otra ........ MultipleInstancesPolicy "IgnoreNew".
      - Tiempo máximo de ejecución ............................ ExecutionTimeLimit (10 min): si se cuelga, Windows la termina.
      - Si falla al iniciar, reintenta ........................ RestartOnFailure (3 veces, cada 1 min). Es una red de seguridad:
                                                                los fallos de SQL ya los cubren los reintentos internos del agente
                                                                y la siguiente ejecución a los 15 minutos.
      - Si el servidor estaba apagado a la hora prevista ...... StartWhenAvailable: se ejecuta en cuanto sea posible.
      - Prioridad baja para no quitarle CPU a Aspel ........... Priority 7 (inferior a la normal; es el valor habitual del Programador).
      - Funciona con batería y sin red "disponible" ........... el servidor no es un portátil; el agente maneja sus propios errores.

.PARAMETER SourcePath
    Carpeta con el programa ya publicado (debe contener SyncAgent.exe). Por omisión: ..\publish junto a este script.
    Se genera con:  dotnet publish src\SyncAgent -c Release -r win-x64 --self-contained -o publish

.PARAMETER InstallPath
    Carpeta de instalación en el servidor. Por omisión: C:\NettesSyncAgent

.PARAMETER AccountName
    Nombre de la cuenta local que ejecuta la tarea. Por omisión: svc-nettes-sync

.PARAMETER IntervalMinutes
    Cada cuántos minutos se ejecuta. Por omisión: 15

.PARAMETER TimeLimitMinutes
    Tiempo máximo que puede tardar UNA ejecución antes de que Windows la termine. Por omisión: 10

.PARAMETER StartupDelayMinutes
    Minutos que espera después de encender el servidor antes del primer disparo. Por omisión: 3

.PARAMETER AccountPassword
    Contraseña de la cuenta como SecureString. Si no se indica, el script la pide por pantalla.

.PARAMETER SkipTestPrompt
    No preguntar al final si se quiere ejecutar la prueba de lectura (--test).

.EXAMPLE
    .\Install-SyncAgent.ps1 -SourcePath C:\Temp\publish

.EXAMPLE
    .\Install-SyncAgent.ps1 -SourcePath C:\Temp\publish -InstallPath D:\NettesSyncAgent -IntervalMinutes 10
#>
# (Esta línea va DESPUÉS del bloque de ayuda a propósito: si va antes, PowerShell 5.1 no reconoce la ayuda de Get-Help.)
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$SourcePath = (Join-Path $PSScriptRoot '..\publish'),
    [string]$InstallPath = 'C:\NettesSyncAgent',
    [ValidatePattern('^[A-Za-z0-9._-]{1,20}$')]
    [string]$AccountName = 'svc-nettes-sync',
    [ValidateRange(1, 1440)]
    [int]$IntervalMinutes = 15,
    [ValidateRange(1, 720)]
    [int]$TimeLimitMinutes = 10,
    [ValidateRange(0, 60)]
    [int]$StartupDelayMinutes = 3,
    [System.Security.SecureString]$AccountPassword,
    [switch]$SkipTestPrompt
)

Set-StrictMode -Version Latest
# Cualquier error detiene el script (en vez de seguir como si nada). Se captura al final para mostrarlo claro.
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------------------------------
# Constantes
# ---------------------------------------------------------------------------------------------------------------------
$TaskName = 'Sincronizacion'
$TaskFolder = '\NettesSyncAgent\'                    # carpeta propia dentro del Programador de tareas
$AccountMarker = 'NettesSyncAgent: cuenta del agente' # descripción de la cuenta (máx. 48 caracteres); sirve para reconocerla al desinstalar
$AdministratorsSid = 'S-1-5-32-544'                  # grupo Administradores (el SID funciona en cualquier idioma de Windows)
$UsersSid = 'S-1-5-32-545'                           # grupo Usuarios
$SystemSid = 'S-1-5-18'                              # cuenta SYSTEM
$TotalSteps = 7

# ---------------------------------------------------------------------------------------------------------------------
# Funciones de ayuda (solo para mostrar mensajes y llamar a programas externos)
# ---------------------------------------------------------------------------------------------------------------------
function Write-Step([int]$Number, [string]$Text) {
    Write-Host ''
    Write-Host "[$Number/$TotalSteps] $Text" -ForegroundColor Cyan
}
function Write-Info([string]$Text) { Write-Host "      $Text" }
function Write-Ok([string]$Text) { Write-Host "      OK     $Text" -ForegroundColor Green }
function Write-Warn([string]$Text) { Write-Host "      AVISO  $Text" -ForegroundColor Yellow }

function ConvertTo-PlainText([System.Security.SecureString]$Secure) {
    # Solo para comparar contraseñas y entregarla al Programador de tareas; no se escribe en pantalla ni en disco.
    return (New-Object System.Net.NetworkCredential('', $Secure)).Password
}

function Invoke-Native([string]$Program, [string[]]$Arguments, [int[]]$OkExitCodes = @(0)) {
    # En PowerShell 5.1, si un programa externo escribe en su salida de error y $ErrorActionPreference es 'Stop',
    # PowerShell lo trata como un error fatal aunque el programa haya salido bien. Por eso se baja a 'Continue'
    # solo durante la llamada y se decide por el CÓDIGO DE SALIDA.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Program @Arguments 2>&1 | Out-String
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($OkExitCodes -notcontains $code) {
        throw "$Program terminó con código $code.`n$output"
    }
    return $output
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

    # Crear cuentas, cambiar permisos y registrar tareas con otra cuenta exige privilegios de administrador.
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $isAdmin = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        throw ('Este script debe ejecutarse en una consola de PowerShell abierta COMO ADMINISTRADOR ' +
               '(clic derecho en PowerShell > "Ejecutar como administrador").')
    }
    Write-Ok "Consola de administrador ($($identity.Name))."

    if (-not (Test-Path -LiteralPath $SourcePath -PathType Container)) {
        throw "No existe la carpeta con el programa publicado: $SourcePath`nGénérela con: dotnet publish src\SyncAgent -c Release -r win-x64 --self-contained -o publish"
    }
    $SourcePath = (Resolve-Path -LiteralPath $SourcePath).Path.TrimEnd('\')
    $sourceExe = Join-Path $SourcePath 'SyncAgent.exe'
    if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
        throw "La carpeta $SourcePath no contiene SyncAgent.exe. ¿Es la carpeta de 'dotnet publish'?"
    }
    Write-Ok "Programa publicado encontrado en $SourcePath"

    $InstallPath = [IO.Path]::GetFullPath($InstallPath).TrimEnd('\')
    if ($InstallPath.Length -le 3) {
        throw "La carpeta de instalación no puede ser la raíz de un disco ($InstallPath)."
    }
    if ($InstallPath -ieq $SourcePath) {
        throw 'La carpeta de instalación y la carpeta publicada no pueden ser la misma.'
    }

    foreach ($cmd in 'New-LocalUser', 'Register-ScheduledTask') {
        if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
            throw "No está disponible el comando $cmd (se necesita Windows PowerShell 5.1 en Windows 10 / Server 2016 o superior)."
        }
    }
    Write-Ok "Sistema: $((Get-CimInstance Win32_OperatingSystem).Caption)"

    if ($IntervalMinutes -le $TimeLimitMinutes) {
        Write-Warn "El intervalo ($IntervalMinutes min) no es mayor que el tiempo máximo de ejecución ($TimeLimitMinutes min)."
        Write-Warn 'No es un error (si una ejecución sigue en curso, la siguiente simplemente no inicia), pero conviene un intervalo mayor.'
    }

    # =================================================================================================================
    Write-Step 2 "Cuenta local dedicada '$AccountName' (sin privilegios de administrador)"
    # =================================================================================================================
    # Por qué una cuenta propia: si el agente tuviera un fallo, el daño se limita a lo que esta cuenta puede hacer
    # (leer el programa y escribir en data\). No es Administrador y su contraseña no se comparte con nadie.

    $account = Get-LocalUser -Name $AccountName -ErrorAction SilentlyContinue

    if (-not $AccountPassword) {
        $AccountPassword = Read-Host -AsSecureString -Prompt "Contraseña para la cuenta '$AccountName' (cumpla la directiva de contraseñas del servidor)"
        if ((ConvertTo-PlainText $AccountPassword).Length -eq 0) { throw 'La contraseña no puede estar vacía.' }
        if ($null -eq $account) {
            $confirmation = Read-Host -AsSecureString -Prompt 'Repita la contraseña'
            if ((ConvertTo-PlainText $AccountPassword) -cne (ConvertTo-PlainText $confirmation)) {
                throw 'Las contraseñas no coinciden.'
            }
        }
    }

    if ($null -eq $account) {
        try {
            New-LocalUser -Name $AccountName -Password $AccountPassword -Description $AccountMarker `
                -PasswordNeverExpires -UserMayNotChangePassword -AccountNeverExpires | Out-Null
        }
        catch {
            throw "No se pudo crear la cuenta '$AccountName': $($_.Exception.Message)`n(Si dice que la contraseña no cumple la directiva, use una más larga y compleja.)"
        }
        Write-Ok "Cuenta '$AccountName' creada."
    }
    else {
        if ($account.Description -ne $AccountMarker) {
            Write-Warn "La cuenta '$AccountName' ya existía y NO fue creada por este instalador. Se reutiliza (se le fija la contraseña indicada)."
        }
        # Se fija siempre la contraseña indicada: así la cuenta y la tarea quedan con la MISMA contraseña.
        Set-LocalUser -Name $AccountName -Password $AccountPassword -PasswordNeverExpires $true
        Enable-LocalUser -Name $AccountName
        Write-Ok "Cuenta '$AccountName' actualizada (contraseña fijada y cuenta habilitada)."
    }

    # La cuenta debe ser un usuario normal: pertenece al grupo Usuarios y NO al de Administradores.
    $usersGroup = Get-LocalGroup -SID ([Security.Principal.SecurityIdentifier]$UsersSid)
    $administratorsGroup = Get-LocalGroup -SID ([Security.Principal.SecurityIdentifier]$AdministratorsSid)

    $inUsers = @(Get-LocalGroupMember -Group $usersGroup.Name -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*\$AccountName" })
    if ($inUsers.Count -eq 0) {
        try {
            Add-LocalGroupMember -Group $usersGroup.Name -Member $AccountName
            Write-Ok "Agregada al grupo '$($usersGroup.Name)'."
        }
        catch {
            # En algunos Windows Get-LocalGroupMember no lista bien el grupo (p. ej. si tiene un miembro huérfano);
            # si en realidad ya era miembro, no es un error.
            if ($_.FullyQualifiedErrorId -like '*MemberExists*') { Write-Ok "Ya pertenecía al grupo '$($usersGroup.Name)'." }
            else { throw }
        }
    }

    $inAdministrators = @(Get-LocalGroupMember -Group $administratorsGroup.Name -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*\$AccountName" })
    if ($inAdministrators.Count -gt 0) {
        throw "La cuenta '$AccountName' pertenece al grupo '$($administratorsGroup.Name)'. El agente NO debe ejecutarse como administrador: quítela de ese grupo o use otro nombre."
    }
    Write-Ok "No pertenece a '$($administratorsGroup.Name)'."

    $accountSid = (Get-LocalUser -Name $AccountName).SID.Value
    $userId = "$env:COMPUTERNAME\$AccountName"

    # Derecho "Iniciar sesión como trabajo por lotes" (SeBatchLogonRight). Una tarea que se ejecuta con contraseña y sin
    # sesión iniciada hace un inicio de sesión "por lotes" (tipo 4). Windows NO concede este derecho automáticamente a una
    # cuenta nueva: sin él la tarea nunca arranca (LastTaskResult 267011 = "aún no se ha ejecutado") y el registro de
    # Seguridad muestra el evento 4625 con el estado 0xC000015B ("no se concedió el tipo de inicio de sesión solicitado").
    # Se concede SOLO a esta cuenta y solo este derecho; el desinstalador lo quita.
    [NettesLsaRights]::Add($accountSid, 'SeBatchLogonRight')
    if (-not [NettesLsaRights]::Has($accountSid, 'SeBatchLogonRight')) {
        throw "No se pudo conceder a '$AccountName' el derecho de inicio de sesión como trabajo por lotes."
    }
    Write-Ok "Derecho 'Iniciar sesión como trabajo por lotes' concedido solo a '$AccountName' (la tarea lo necesita para arrancar sin sesión iniciada)."

    # =================================================================================================================
    Write-Step 3 "Copiar el programa a $InstallPath"
    # =================================================================================================================

    $existingTask = Get-ScheduledTask -TaskName $TaskName -TaskPath $TaskFolder -ErrorAction SilentlyContinue
    if ($null -ne $existingTask) {
        # Si es una actualización, hay que parar la tarea (y deshabilitarla mientras se copia) para que los archivos no estén en uso.
        if ($existingTask.State -eq 'Running') {
            Stop-ScheduledTask -TaskName $TaskName -TaskPath $TaskFolder
            Write-Info 'Se detuvo la ejecución en curso de la tarea.'
        }
        Disable-ScheduledTask -TaskName $TaskName -TaskPath $TaskFolder | Out-Null
        Write-Info 'Tarea existente deshabilitada durante la copia (se vuelve a habilitar en el paso 6).'
    }

    $running = @(Get-Process -Name 'SyncAgent' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -like "$InstallPath\*" })
    if ($running.Count -gt 0) {
        Write-Info 'Esperando a que termine la ejecución en curso del agente (máximo 60 s)...'
        $running | ForEach-Object { [void]$_.WaitForExit(60000) }
        if (@(Get-Process -Name 'SyncAgent' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -like "$InstallPath\*" }).Count -gt 0) {
            throw 'El agente sigue en ejecución. Espere a que termine o ciérrelo desde el Administrador de tareas y vuelva a ejecutar este script.'
        }
    }

    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null

    # robocopy /MIR deja la carpeta destino idéntica a la de origen (copia lo nuevo y borra lo que sobra) EXCEPTO lo excluido:
    #   /XD data                          no toca la carpeta data\ (estado, candado y logs de ejecuciones anteriores)
    #   /XF appsettings.Production.json   no toca la configuración con la contraseña (se conserva entre actualizaciones)
    # Códigos de salida de robocopy: 0 a 7 = correcto (distintas combinaciones de "copió / no había nada que copiar"); 8 o más = error.
    $robocopyArgs = @($SourcePath, $InstallPath, '/MIR', '/XD', 'data', '/XF', 'appsettings.Production.json',
                      '/R:2', '/W:2', '/NFL', '/NDL', '/NJH', '/NP')
    [void](Invoke-Native -Program 'robocopy.exe' -Arguments $robocopyArgs -OkExitCodes @(0, 1, 2, 3, 4, 5, 6, 7))
    Write-Ok 'Programa copiado (la configuración y los datos existentes no se tocaron).'

    $dataPath = Join-Path $InstallPath 'data'
    New-Item -ItemType Directory -Path (Join-Path $dataPath 'logs') -Force | Out-Null
    Write-Ok "Carpeta de datos lista: $dataPath (estado, candado y logs)."

    $exePath = Join-Path $InstallPath 'SyncAgent.exe'
    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) { throw "Después de copiar no se encuentra $exePath." }

    # =================================================================================================================
    Write-Step 4 'Permisos NTFS (mínimo privilegio)'
    # =================================================================================================================
    # Resultado buscado:
    #   - Administradores y SYSTEM ........ control total (para mantener el programa).
    #   - La cuenta del agente ............ SOLO LECTURA y ejecución del programa, y MODIFICAR únicamente en data\.
    #   - Los demás usuarios del servidor .. sin acceso.
    # Se usan SIDs (con "*") para que funcione igual en Windows en español o en inglés.
    # (OI)(CI) = el permiso se hereda a archivos y subcarpetas.  F = control total, RX = leer y ejecutar, M = modificar, R = leer.

    # 4a. Raíz: se corta la herencia (/inheritance:r) para que no se hereden permisos del disco, y se conceden solo los tres.
    [void](Invoke-Native -Program 'icacls.exe' -Arguments @($InstallPath, '/inheritance:r', '/grant:r',
        "*${AdministratorsSid}:(OI)(CI)F", "*${SystemSid}:(OI)(CI)F", "*${accountSid}:(OI)(CI)RX"))

    # 4b. Se restablece lo que hay dentro para que HEREDE de la raíz (limpia permisos sueltos de instalaciones anteriores).
    [void](Invoke-Native -Program 'icacls.exe' -Arguments @("$InstallPath\*", '/reset', '/T', '/C', '/Q'))

    # 4c. data\: ahí sí puede escribir (estado, candado, logs). Es la ÚNICA carpeta con permiso de modificar.
    [void](Invoke-Native -Program 'icacls.exe' -Arguments @($dataPath, '/grant:r', "*${accountSid}:(OI)(CI)M"))
    Write-Ok "Cuenta del agente: lectura y ejecución en $InstallPath; modificación solo en $dataPath."

    # =================================================================================================================
    Write-Step 5 'Archivo de configuración appsettings.Production.json'
    # =================================================================================================================

    $configPath = Join-Path $InstallPath 'appsettings.Production.json'
    $examplePath = Join-Path $InstallPath 'appsettings.Production.example.json'
    $configWasCreated = $false

    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        if (-not (Test-Path -LiteralPath $examplePath -PathType Leaf)) {
            throw "No se encuentra el archivo de ejemplo $examplePath."
        }
        Copy-Item -LiteralPath $examplePath -Destination $configPath
        $configWasCreated = $true
        Write-Ok 'Se creó appsettings.Production.json a partir del ejemplo.'
    }
    else {
        Write-Ok 'appsettings.Production.json ya existía: se conserva tal cual.'
    }

    # Este archivo contiene la contraseña de SQL Server: solo Administradores y SYSTEM pueden leerlo y modificarlo;
    # la cuenta del agente solo puede LEERLO (no puede cambiar su propia configuración).
    [void](Invoke-Native -Program 'icacls.exe' -Arguments @($configPath, '/inheritance:r', '/grant:r',
        "*${AdministratorsSid}:F", "*${SystemSid}:F", "*${accountSid}:R"))
    Write-Ok 'Archivo protegido: escritura solo para Administradores; la cuenta del agente solo lo lee.'

    $configNeedsEdit = Select-String -LiteralPath $configPath -Pattern 'Password=CAMBIAR' -SimpleMatch -Quiet
    if ($configNeedsEdit) {
        Write-Warn 'La cadena de conexión todavía tiene la contraseña de ejemplo ("CAMBIAR"). Hay que editarla (ver el resumen final).'
    }

    # =================================================================================================================
    Write-Step 6 "Registrar la tarea programada $TaskFolder$TaskName"
    # =================================================================================================================
    # Se define la tarea con XML (el mismo formato que usa el Programador de tareas por dentro). Así cada opción de abajo
    # corresponde a una casilla de la ventana del Programador y no depende de cómo se comporten los cmdlets en cada versión de Windows.
    # Las tareas se escriben en hora LOCAL del servidor.

    $firstRun = (Get-Date).AddMinutes(2).ToString('yyyy-MM-ddTHH:mm:ss')
    $e = { param($text) [Security.SecurityElement]::Escape($text) }   # escapa &, <, > y comillas dentro del XML

    $taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Agente de sincronizacion de Mini Super Nettes: lee Aspel SAE (solo lectura) y guarda el estado en data\. Instalado por Install-SyncAgent.ps1.</Description>
  </RegistrationInfo>
  <Triggers>
    <!-- Disparo 1: unos minutos despues de ENCENDER el servidor (da tiempo a que arranquen SQL Server y Aspel). -->
    <BootTrigger>
      <Enabled>true</Enabled>
      <Delay>PT$($StartupDelayMinutes)M</Delay>
    </BootTrigger>
    <!-- Disparo 2: empieza 2 minutos despues de instalar y se REPITE cada $IntervalMinutes minutos. Sin <Duration> = para siempre. -->
    <TimeTrigger>
      <Enabled>true</Enabled>
      <StartBoundary>$firstRun</StartBoundary>
      <Repetition>
        <Interval>PT$($IntervalMinutes)M</Interval>
      </Repetition>
    </TimeTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>$(& $e $userId)</UserId>
      <!-- Password = se ejecuta con la contraseña de la cuenta, AUNQUE NO HAYA NINGUNA SESION INICIADA. -->
      <LogonType>Password</LogonType>
      <!-- LeastPrivilege = sin elevar privilegios (la cuenta ni siquiera es administradora). -->
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <!-- IgnoreNew = si la ejecucion anterior sigue en curso, NO se inicia otra. -->
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <!-- Es un servidor: que funcione igual con o sin bateria y sin esperar a que haya "red disponible". -->
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <!-- StartWhenAvailable = si a la hora prevista el servidor estaba apagado, se ejecuta en cuanto sea posible. -->
    <StartWhenAvailable>true</StartWhenAvailable>
    <!-- Tiempo maximo de UNA ejecucion; pasado este limite Windows la termina (una lectura normal dura segundos). -->
    <ExecutionTimeLimit>PT$($TimeLimitMinutes)M</ExecutionTimeLimit>
    <AllowHardTerminate>true</AllowHardTerminate>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <!-- Prioridad 7 = inferior a la normal: el agente cede CPU a Aspel y a SQL Server. -->
    <Priority>7</Priority>
    <!-- Red de seguridad: si la tarea falla, Windows la reintenta 3 veces, cada 1 minuto. -->
    <RestartOnFailure>
      <Interval>PT1M</Interval>
      <Count>3</Count>
    </RestartOnFailure>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$(& $e $exePath)</Command>
      <!-- El argumento "once" (con dos guiones delante) = una sincronizacion completa y terminar: no queda ningun proceso residente entre ejecuciones. -->
      <Arguments>--once</Arguments>
      <!-- El Programador de tareas arranca en System32 por omision; el agente busca sus archivos junto al .exe. -->
      <WorkingDirectory>$(& $e $InstallPath)</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
"@

    # Register-ScheduledTask valida la contraseña contra la cuenta: si no coincide, falla aquí y no más tarde en silencio.
    # -Force = si la tarea ya existe, la REEMPLAZA (por eso el script se puede ejecutar varias veces).
    $plainPassword = ConvertTo-PlainText $AccountPassword
    try {
        Register-ScheduledTask -Xml $taskXml -TaskName $TaskName -TaskPath $TaskFolder -User $userId -Password $plainPassword -Force | Out-Null
    }
    finally {
        $plainPassword = $null
    }

    $task = Get-ScheduledTask -TaskName $TaskName -TaskPath $TaskFolder
    Write-Ok "Tarea registrada: $TaskFolder$TaskName (estado: $($task.State))."
    Write-Info "Cuenta ............ $($task.Principal.UserId)  (inicio de sesión: $($task.Principal.LogonType), nivel: $($task.Principal.RunLevel))"
    foreach ($trigger in $task.Triggers) {
        $kind = $trigger.CimClass.CimClassName -replace '^MSFT_Task', '' -replace 'Trigger$', ''
        $repetition = ''
        if ($trigger.Repetition -and $trigger.Repetition.Interval) { $repetition = "; se repite cada $($trigger.Repetition.Interval) (sin fin)" }
        $delay = ''
        if (($trigger.PSObject.Properties.Name -contains 'Delay') -and $trigger.Delay) { $delay = "; retraso $($trigger.Delay)" }
        Write-Info "Disparador ........ $kind$delay$repetition"
    }
    Write-Info "Instancias ........ $($task.Settings.MultipleInstances)  |  límite de ejecución: $($task.Settings.ExecutionTimeLimit)  |  prioridad: $($task.Settings.Priority)"

    # =================================================================================================================
    Write-Step 7 'Resumen'
    # =================================================================================================================

    Write-Host ''
    Write-Host '      Programa ............ ' -NoNewline; Write-Host $exePath
    Write-Host '      Configuración ....... ' -NoNewline; Write-Host $configPath
    Write-Host '      Estado de la sync ... ' -NoNewline; Write-Host (Join-Path $dataPath 'sync-state.json')
    Write-Host '      Logs (14 días) ...... ' -NoNewline; Write-Host (Join-Path $dataPath 'logs')
    Write-Host ''
    Write-Host '      PRÓXIMOS PASOS' -ForegroundColor Cyan
    if ($configNeedsEdit) {
        Write-Host "      1. Edite $configPath (como administrador) y ponga la contraseña real de 'catalogo_lectura'." -ForegroundColor Yellow
        Write-Host '         La cuenta de SQL Server se crea antes con sql\install\01-crear-login-lectura.sql.' -ForegroundColor Yellow
    }
    else {
        Write-Host '      1. La cadena de conexión ya está configurada.'
    }
    Write-Host "      2. Pruebe la lectura (no envía nada ni escribe en disco):  & '$exePath' --test"
    Write-Host "      3. Verifique la tarea:   Get-ScheduledTask -TaskPath '$TaskFolder' | Get-ScheduledTaskInfo"
    Write-Host "      4. Verifique permisos:   icacls '$InstallPath'   y   icacls '$dataPath'"
    Write-Host "      5. Para ejecutarla ya:   Start-ScheduledTask -TaskPath '$TaskFolder' -TaskName '$TaskName'"
    Write-Host "      6. Para desinstalar:     & '$(Join-Path $PSScriptRoot 'Uninstall-SyncAgent.ps1')' -InstallPath '$InstallPath' -AccountName '$AccountName'"

    if (-not $SkipTestPrompt -and -not $configNeedsEdit) {
        Write-Host ''
        $answer = Read-Host '¿Ejecutar ahora la prueba de lectura (SyncAgent.exe --test)? [s/N]'
        if ($answer -match '^(s|si|sí|y|yes)$') {
            Write-Host ''
            & $exePath --test
            Write-Host ''
            Write-Info "Código de salida de la prueba: $LASTEXITCODE (0 = correcto)."
        }
    }

    Write-Host ''
    Write-Host 'Instalación terminada.' -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ''
    Write-Host 'ERROR: la instalación no se completó.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host 'El script se puede volver a ejecutar: actualiza lo que ya exista en vez de fallar.' -ForegroundColor Yellow
    exit 1
}
