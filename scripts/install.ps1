# Instala GoPilot (servicio + widget) en la Legion Go S. Lo más fácil: doble clic en «Instalar GoPilot.cmd».
# También desde PowerShell como administrador en la carpeta descomprimida.
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
try {
    & (Join-Path $PSScriptRoot 'install-service.ps1')
    & (Join-Path $PSScriptRoot 'install-widget.ps1')
    Write-Host ''
    Write-Host 'GoPilot instalado. Abre Game Bar (Win + G o Legion R) > Widgets > GoPilot.' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host "No se pudo instalar GoPilot: $($_.Exception.Message)" -ForegroundColor Red
}
# Abierto con doble clic la ventana se cerraría sola: se espera para que se pueda leer el resultado
if ([Environment]::UserInteractive -and -not $env:SSH_CONNECTION) { Read-Host 'Pulsa Intro para cerrar' | Out-Null }
