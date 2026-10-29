<#
.SYNOPSIS
    Compila Colmillo y publica un release en GitHub con Colmillo.exe e instalar.ps1.
.DESCRIPTION
    La version sale de <Version> en Colmillo.csproj; el tag es "v<version>". Usa la
    credencial de GitHub que Git ya guardo (Git Credential Manager): no pide token.
    Para una version nueva: subir <Version>, hacer commit y correr este script.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\publicar-release.ps1
#>
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
Set-Location $raiz
$repo = 'MesshM/Colmillo-'

$version = ([xml](Get-Content Colmillo.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$tag = "v$version"
Write-Host "Publicando Colmillo $tag"

# 1. Compilar
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o publish
if ($LASTEXITCODE -ne 0) { throw 'La compilacion fallo' }

# 2. Subir commits y tag
if (git status --porcelain) { throw 'Hay cambios sin commit: hacer commit antes de publicar.' }
git push origin main
if (-not (git tag -l $tag)) { git tag -a $tag -m "Colmillo $version" }
git push origin $tag

# 3. Credencial guardada por Git (la misma que usa git push)
$cred = "protocol=https`nhost=github.com`n`n" | git credential fill
$token = ($cred | Where-Object { $_ -like 'password=*' }) -replace '^password=', ''
if (-not $token) { throw 'No hay credencial de GitHub guardada. Hacer un git push primero.' }
$h = @{ Authorization = "token $token"; Accept = 'application/vnd.github+json'; 'User-Agent' = 'colmillo-release' }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# 4. Crear el release (o reusar si ya existe ese tag)
try {
    $rel = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/tags/$tag" -Headers $h
    Write-Host "El release $tag ya existia; se reemplazan sus archivos."
} catch {
    # -Encoding UTF8: PowerShell 5.1 lee por defecto en ANSI y romperia las tildes.
    $notas = Get-Content (Join-Path $PSScriptRoot 'notas-release.md') -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
    if (-not $notas) { $notas = "Colmillo $version" }
    $body = @{ tag_name = $tag; name = "Colmillo $version"; body = $notas; draft = $false; prerelease = $false } | ConvertTo-Json
    $rel = Invoke-RestMethod "https://api.github.com/repos/$repo/releases" -Method Post -Headers $h -Body ([Text.Encoding]::UTF8.GetBytes($body)) -ContentType 'application/json; charset=utf-8'
}

# 5. Subir archivos (los nombres fijos permiten el enlace .../releases/latest/download/<archivo>)
foreach ($archivo in @("$raiz\publish\Colmillo.exe", "$raiz\scripts\instalar.ps1")) {
    $nombre = Split-Path $archivo -Leaf
    $viejo = $rel.assets | Where-Object { $_.name -eq $nombre }
    if ($viejo) { Invoke-RestMethod "https://api.github.com/repos/$repo/releases/assets/$($viejo.id)" -Method Delete -Headers $h | Out-Null }
    $url = "https://uploads.github.com/repos/$repo/releases/$($rel.id)/assets?name=$nombre"
    Invoke-RestMethod $url -Method Post -Headers $h -InFile $archivo -ContentType 'application/octet-stream' | Out-Null
    Write-Host "Subido $nombre"
}
Write-Host "Listo: $($rel.html_url)"
