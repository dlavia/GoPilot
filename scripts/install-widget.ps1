# Instala el widget en la Legion Go S. Ejecutar como administrador desde la carpeta dist\.
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

# Confiar en el certificado de desarrollo (solo hace falta la primera vez).
Import-Certificate -FilePath (Join-Path $PSScriptRoot 'GoSPanel.cer') -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

$msix = Get-ChildItem (Join-Path $PSScriptRoot 'widget') -Recurse -Filter '*.msix' | Select-Object -First 1
if (-not $msix) { throw 'No se encontró el .msix en widget\' }
# .NET Native y VCLibs: vienen junto al paquete por si el equipo no los tiene
$deps = @(Get-ChildItem (Join-Path $msix.DirectoryName 'Dependencies\x64') -Filter '*.appx' -ErrorAction SilentlyContinue | ForEach-Object FullName)
if ($deps) { Add-AppxPackage -Path $msix.FullName -DependencyPath $deps -ForceApplicationShutdown -ForceUpdateFromAnyVersion }
else { Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown -ForceUpdateFromAnyVersion }
Write-Host 'Widget instalado. Abre Game Bar (Win+G) > Widgets > GoPilot.'
