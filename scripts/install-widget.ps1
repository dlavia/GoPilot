# Instala el widget en la Legion Go S. Ejecutar como administrador desde la carpeta dist\.
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

# Confiar en el certificado de desarrollo (solo hace falta la primera vez).
Import-Certificate -FilePath (Join-Path $PSScriptRoot 'GoSPanel.cer') -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

$msix = Get-ChildItem (Join-Path $PSScriptRoot 'widget') -Recurse -Filter '*.msix' | Select-Object -First 1
if (-not $msix) { throw 'No se encontró el .msix en widget\' }
Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown -ForceUpdateFromAnyVersion
Write-Host 'Widget instalado. Abre Game Bar (Win+G) > Widgets > GoPilot.'
