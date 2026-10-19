# Actualiza el ejecutable del servicio instalado con el de la carpeta service\ junto a este script.
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$target = "$env:ProgramFiles\GoSPanel\Service"

Stop-Service GoSPanel
(Get-Service GoSPanel).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
Get-Process GoSPanel.Service -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Copy-Item (Join-Path $PSScriptRoot 'service\*') $target -Recurse -Force
Start-Service GoSPanel
Get-Service GoSPanel | Select-Object Name, Status
