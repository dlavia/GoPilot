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
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip
# Lo que leen los widgets instalados para saber si hay versión nueva (Updater.cs)
$latest = Join-Path $root 'latest.json'
[ordered]@{ version = $version; notes = $Notes; size = (Get-Item $zip).Length } | ConvertTo-Json |
    Set-Content $latest -Encoding UTF8

# La versión que compila build.ps1 queda en el manifiesto: se guarda con su etiqueta.
git -C $root add src/GoSPanel.Widget/Package.appxmanifest
git -C $root commit -m "GoPilot $version" | Out-Null
git -C $root tag "v$version"
git -C $root push origin HEAD "v$version"
& $gh release create "v$version" $zip $latest --repo dlavia/GoPilot --title "GoPilot $version" --notes $Notes
if ($LASTEXITCODE) { throw 'No se pudo publicar la versión' }
Remove-Item $zip, $latest
Write-Host "Publicada GoPilot $version"
