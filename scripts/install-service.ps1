# Instala (o actualiza) el servicio GoSPanel en la Legion Go S.
# Ejecutar en PowerShell como administrador, desde la carpeta dist\ copiada a la consola.
#Requires -RunAsAdministrator
param(
    [string]$Source = (Join-Path $PSScriptRoot 'service'),
    [string]$Target = "$env:ProgramFiles\GoSPanel\Service"
)

$ErrorActionPreference = 'Stop'
$exe = Join-Path $Target 'GoSPanel.Service.exe'

if (Get-Service GoSPanel -ErrorAction SilentlyContinue) {
    Stop-Service GoSPanel -Force
}

New-Item -ItemType Directory -Force $Target | Out-Null
Copy-Item (Join-Path $Source '*') $Target -Recurse -Force

if (-not (Get-Service GoSPanel -ErrorAction SilentlyContinue)) {
    New-Service -Name GoSPanel -BinaryPathName "`"$exe`"" -DisplayName 'GoPilot' `
        -Description 'Backend del widget GoPilot (TDP, ventilador, sensores).' -StartupType Automatic | Out-Null
    # Reinicio automático si se cae.
    sc.exe failure GoSPanel reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
}

Start-Service GoSPanel
Get-Service GoSPanel
