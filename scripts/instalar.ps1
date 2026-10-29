<#
.SYNOPSIS
    Instala o actualiza Colmillo y, opcionalmente, evita que Windows instale Razer Synapse.

.DESCRIPTION
    Sin argumentos baja la ultima version publicada en GitHub y la instala en
    %LOCALAPPDATA%\Programs\Colmillo (correrlo de nuevo = actualizar). Los ajustes viven
    en %APPDATA%\Colmillo y no se tocan.

    Por que Windows instala Synapse solo: al conectar un Razer Cobra, Windows Update
    baja el driver "rz00a3dev.inf" de Razer. Ese paquete trae un co-instalador
    (RazerS3CoinstallerEx.dll) que ejecuta RzS3WizardPkgS3.exe, y eso abre el instalador
    de Synapse. Con -BloquearSynapse (pide administrador):

      1. Windows deja de buscar drivers en Windows Update al conectar dispositivos
         (DriverSearching\SearchOrderConfig = 0) y de bajar apps/iconos del fabricante
         (Device Metadata\PreventDeviceMetadataFromNetwork = 1).
      2. Windows Update deja de ofrecer drivers en las actualizaciones
         (politica ExcludeWUDriversInQualityUpdate = 1). Afecta a TODOS los drivers:
         los de video, chipset, etc. hay que bajarlos a mano desde el fabricante.
      3. Si el paquete de Razer con el co-instalador ya esta en el equipo, lo borra.
         Ese paquete solo maneja la interfaz MI_02 del mouse (teclas especiales), que
         vuelve al driver HID de Windows. Colmillo usa MI_00 y MI_01, no MI_02.

.PARAMETER Exe
    Instalar este Colmillo.exe en vez de bajar el ultimo.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\instalar.ps1
    powershell -ExecutionPolicy Bypass -File .\instalar.ps1 -BloquearSynapse
#>
param(
    [switch]$BloquearSynapse,
    [string]$Exe,
    # Uso interno: la copia elevada solo hace el bloqueo y sale.
    [switch]$SoloBloqueo
)

$ErrorActionPreference = 'Stop'
$repo = 'MesshM/Colmillo-'
$destino = Join-Path $env:LOCALAPPDATA 'Programs\Colmillo'

function Es-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal $id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Bloquear-Synapse {
    New-Item -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching' -Force | Out-Null
    Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching' SearchOrderConfig 0 -Type DWord
    New-Item -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata' -Force | Out-Null
    Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata' PreventDeviceMetadataFromNetwork 1 -Type DWord
    New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' -Force | Out-Null
    Set-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' ExcludeWUDriversInQualityUpdate 1 -Type DWord
    Write-Host 'Windows ya no baja drivers ni apps de fabricante por su cuenta.'

    # Paquetes de Razer con el co-instalador que abre Synapse
    $store = 'C:\Windows\System32\DriverStore\FileRepository'
    $conInstalador = Get-ChildItem $store -Directory -Filter 'rz*' |
        Where-Object { Test-Path (Join-Path $_.FullName 'RazerS3CoinstallerEx.dll') } |
        ForEach-Object { (Get-ChildItem $_.FullName -Filter *.inf | Select-Object -First 1).Name }
    if ($conInstalador) {
        $publicados = pnputil /enum-drivers | Out-String
        foreach ($inf in $conInstalador) {
            # pnputil identifica el paquete por su nombre publicado (oemNN.inf)
            $m = [regex]::Match($publicados, "Published Name:\s+(oem\d+\.inf)\s+Original Name:\s+$([regex]::Escape($inf))")
            if ($m.Success) {
                pnputil /delete-driver $m.Groups[1].Value /uninstall /force | Out-Null
                Write-Host "Quitado el paquete de Razer $inf ($($m.Groups[1].Value))"
            }
        }
    } else {
        Write-Host 'No hay paquetes de Razer con el instalador de Synapse.'
    }
    $wizard = Join-Path ${env:ProgramFiles(x86)} 'Razer\RzS3WizardPkg'
    if (Test-Path $wizard) { Remove-Item $wizard -Recurse -Force; Write-Host 'Quitado el asistente de instalacion de Synapse.' }
}

if ($SoloBloqueo) {
    Bloquear-Synapse
    exit
}

# ---------- 1. Bloqueo (en una copia elevada aparte) ----------
# Colmillo nunca tiene que quedar corriendo como administrador: el resto del script
# sigue sin elevar.
if ($BloquearSynapse) {
    if (Es-Admin) {
        Bloquear-Synapse
    } else {
        Write-Host 'Pidiendo permisos de administrador para bloquear Synapse...'
        # powershell.exe y no pwsh: un Windows recien instalado solo trae PowerShell 5.1.
        $p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-SoloBloqueo'
        if ($p.ExitCode -ne 0) { Write-Warning 'El bloqueo no termino bien; Colmillo se instala igual.' }
    }
}

# ---------- 2. Runtime de escritorio de .NET 9 ----------
$tieneRuntime = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $tieneRuntime = [bool](dotnet --list-runtimes | Select-String 'Microsoft.WindowsDesktop.App 9\.')
}
if (-not $tieneRuntime) {
    Write-Host 'Instalando el runtime de escritorio de .NET 9 (winget)...'
    winget install --id Microsoft.DotNet.DesktopRuntime.9 --source winget --accept-package-agreements --accept-source-agreements
}

# ---------- 3. Conseguir Colmillo.exe ----------
if (-not $Exe) {
    $Exe = Join-Path $env:TEMP 'Colmillo-descarga.exe'
    # PowerShell 5.1 todavia ofrece TLS 1.0 por defecto y GitHub lo rechaza.
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Write-Host "Bajando la ultima version de github.com/$repo ..."
    Invoke-WebRequest "https://github.com/$repo/releases/latest/download/Colmillo.exe" -OutFile $Exe -UseBasicParsing
}
if (-not (Test-Path $Exe)) { throw "No se encontro $Exe" }

$nueva = (Get-Item $Exe).VersionInfo.FileVersion
$instalado = Join-Path $destino 'Colmillo.exe'
$anterior = if (Test-Path $instalado) { (Get-Item $instalado).VersionInfo.FileVersion } else { $null }

# ---------- 4. Instalar ----------
Get-Process Colmillo -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
New-Item -ItemType Directory -Force $destino | Out-Null
Copy-Item $Exe $instalado -Force

# Acceso directo en el menu Inicio
$lnk = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Colmillo.lnk'))
$lnk.TargetPath = $instalado
$lnk.Save()

# Colmillo registra solo el inicio con Windows al arrancar (segun su ajuste).
Start-Process $instalado
if ($anterior) { Write-Host "Colmillo actualizado: $anterior -> $nueva" } else { Write-Host "Colmillo $nueva instalado en $destino" }
