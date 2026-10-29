<#
.SYNOPSIS
    Instala Colmillo y, opcionalmente, evita que Windows instale Razer Synapse solo.

.DESCRIPTION
    Al conectar un Razer Cobra, Windows Update baja el driver "rz00a3dev.inf" de Razer.
    Ese paquete trae un co-instalador (RazerS3CoinstallerEx.dll) que ejecuta
    RzS3WizardPkgS3.exe, y eso abre el instalador de Synapse. Con -BloquearSynapse:

      1. Windows deja de buscar drivers en Windows Update al conectar dispositivos
         (DriverSearching\SearchOrderConfig = 0) y de bajar apps/iconos del fabricante
         (Device Metadata\PreventDeviceMetadataFromNetwork = 1).
      2. Windows Update deja de ofrecer drivers en las actualizaciones
         (politica ExcludeWUDriversInQualityUpdate = 1). Afecta a TODOS los drivers:
         los de video, chipset, etc. hay que bajarlos a mano desde el fabricante.
      3. Si el paquete de Razer con el co-instalador ya esta en el equipo, lo borra.
         Ese paquete solo maneja la interfaz MI_02 del mouse (teclas especiales), que
         vuelve al driver HID de Windows. Colmillo usa MI_00 y MI_01, no MI_02.

    Correrlo ANTES de conectar el mouse en una instalacion nueva de Windows.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\instalar.ps1
    powershell -ExecutionPolicy Bypass -File .\instalar.ps1 -BloquearSynapse     (pide administrador)
#>
param([switch]$BloquearSynapse)

$ErrorActionPreference = 'Stop'
$destino = Join-Path $env:LOCALAPPDATA 'Programs\Colmillo'

function Es-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal $id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# ---------- 1. Runtime de escritorio de .NET 9 ----------
$tieneRuntime = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $tieneRuntime = [bool](dotnet --list-runtimes | Select-String 'Microsoft.WindowsDesktop.App 9\.')
}
if (-not $tieneRuntime) {
    Write-Host 'Instalando el runtime de escritorio de .NET 9 (winget)...'
    winget install --id Microsoft.DotNet.DesktopRuntime.9 --source winget --accept-package-agreements --accept-source-agreements
}

# ---------- 2. Copiar Colmillo ----------
$exe = @("$PSScriptRoot\Colmillo.exe", "$PSScriptRoot\..\publish\Colmillo.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $exe) {
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        Write-Host 'No hay Colmillo.exe compilado; compilando desde el codigo...'
        Push-Location "$PSScriptRoot\.."
        dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o publish
        Pop-Location
        $exe = "$PSScriptRoot\..\publish\Colmillo.exe"
    } else {
        throw 'No se encontro Colmillo.exe junto al script ni el SDK de .NET para compilarlo.'
    }
}
Get-Process Colmillo -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Force $destino | Out-Null
Copy-Item $exe (Join-Path $destino 'Colmillo.exe') -Force
Write-Host "Colmillo copiado a $destino"

# Acceso directo en el menu Inicio
$inicio = Join-Path ([Environment]::GetFolderPath('Programs')) 'Colmillo.lnk'
$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut($inicio)
$lnk.TargetPath = Join-Path $destino 'Colmillo.exe'
$lnk.Save()

# ---------- 3. Bloquear Synapse (opcional) ----------
if ($BloquearSynapse) {
    if (-not (Es-Admin)) {
        Write-Host 'Pidiendo permisos de administrador para bloquear Synapse...'
        # powershell.exe y no pwsh: un Windows recien instalado solo trae PowerShell 5.1.
        Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-BloquearSynapse'
        exit
    }

    New-Item -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching' -Force | Out-Null
    Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching' SearchOrderConfig 0 -Type DWord
    New-Item -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata' -Force | Out-Null
    Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata' PreventDeviceMetadataFromNetwork 1 -Type DWord
    New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' -Force | Out-Null
    Set-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' ExcludeWUDriversInQualityUpdate 1 -Type DWord
    Write-Host 'Windows ya no baja drivers ni apps de fabricante por su cuenta.'

    # Paquetes de Razer con el co-instalador que abre Synapse
    $repo = 'C:\Windows\System32\DriverStore\FileRepository'
    $conInstalador = Get-ChildItem $repo -Directory -Filter 'rz*' |
        Where-Object { Test-Path (Join-Path $_.FullName 'RazerS3CoinstallerEx.dll') } |
        ForEach-Object { (Get-ChildItem $_.FullName -Filter *.inf | Select-Object -First 1).Name }
    if ($conInstalador) {
        $publicados = pnputil /enum-drivers | Out-String
        foreach ($inf in $conInstalador) {
            # pnputil identifica el paquete por su nombre publicado (oemNN.inf)
            $bloque = [regex]::Match($publicados, "Published Name:\s+(oem\d+\.inf)\s+Original Name:\s+$([regex]::Escape($inf))")
            if ($bloque.Success) {
                pnputil /delete-driver $bloque.Groups[1].Value /uninstall /force | Out-Null
                Write-Host "Quitado el paquete de Razer $inf ($($bloque.Groups[1].Value))"
            }
        }
    }
    $wizard = Join-Path ${env:ProgramFiles(x86)} 'Razer\RzS3WizardPkg'
    if (Test-Path $wizard) { Remove-Item $wizard -Recurse -Force; Write-Host 'Quitado el asistente de instalacion de Synapse.' }
}

Start-Process (Join-Path $destino 'Colmillo.exe')
Write-Host 'Listo. Colmillo queda en la bandeja y arranca con Windows.'
