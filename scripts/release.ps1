# Compila y publica una versión nueva en GitHub (Releases de dlavia/GoPilot). Los widgets instalados la ven
# en Ajustes › Actualizaciones. El .zip sirve también para instalar desde cero (install.ps1).
#   .\scripts\release.ps1 -Notes "- Novedad 1`n- Novedad 2"
param([Parameter(Mandatory)][string]$Notes)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dist = Join-Path $root 'dist'
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = "$env:ProgramFiles\GitHub CLI\gh.exe" }

& (Join-Path $PSScriptRoot 'build.ps1')
$version = ([xml](Get-Content (Join-Path $root 'src\GoSPanel.Widget\Package.appxmanifest') -Raw)).Package.Identity.Version
$zip = Join-Path $root "GoPilot-$version.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
# Solo lo necesario en una Legion Go S (x64): sin símbolos, dependencias de otras arquitecturas ni el instalador de VS.
$stage = Join-Path $env:TEMP "gopilot-release-$version"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $dist $stage -Recurse
$package = Get-ChildItem (Join-Path $stage 'widget') -Directory
if (@($package).Count -ne 1 -or -not (Test-Path (Join-Path $package.FullName "*_$($version)_x64.msix"))) { throw "dist\widget debe tener solo la versión $version" }
Get-ChildItem $package.FullName -Exclude '*.msix', '*.cer', 'Dependencies' | Remove-Item -Recurse -Force
Get-ChildItem (Join-Path $package.FullName 'Dependencies') -Exclude 'x64' | Remove-Item -Recurse -Force
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item $stage -Recurse -Force
# Lo que leen los widgets instalados para saber si hay versión nueva (Updater.cs)
$latest = Join-Path $root 'latest.json'
[ordered]@{ version = $version; notes = $Notes; size = (Get-Item $zip).Length } | ConvertTo-Json |
    Set-Content $latest -Encoding UTF8

# La versión que compila build.ps1 queda en el manifiesto: se guarda con su etiqueta.
# (git escribe avisos en stderr que PowerShell 5.1 trataría como errores con 'Stop')
$ErrorActionPreference = 'Continue'
git -C $root add src/GoSPanel.Widget/Package.appxmanifest 2>$null
git -C $root commit -q -m "GoPilot $version" 2>$null
git -C $root tag "v$version"
git -C $root push -q origin HEAD "v$version" 2>$null
if ($LASTEXITCODE) { throw 'No se pudo subir la versión a GitHub' }
& $gh release create "v$version" $zip $latest --repo dlavia/GoPilot --title "GoPilot $version" --notes $Notes
if ($LASTEXITCODE) { throw 'No se pudo publicar la versión' }
Remove-Item $zip, $latest
Write-Host "Publicada GoPilot $version"
