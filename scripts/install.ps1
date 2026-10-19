# Instala GoPilot (servicio + widget) en la Legion Go S. Clic derecho › «Ejecutar con PowerShell» como
# administrador, o desde PowerShell como administrador en la carpeta descomprimida.
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'install-service.ps1')
& (Join-Path $PSScriptRoot 'install-widget.ps1')
