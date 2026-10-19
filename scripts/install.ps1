# Instala GoPilot (servicio + widget) en la Legion Go S. Lo más fácil: doble clic en «Instalar GoPilot.cmd».
# También desde PowerShell como administrador en la carpeta descomprimida.
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
try {
    & (Join-Path $PSScriptRoot 'install-service.ps1')
    # GoPilot sustituye a Legion Space (botones Legion, icono de Game Bar); al desinstalar vuelve a quedar activado
    Start-Process (Join-Path $env:ProgramFiles 'GoSPanel\Service\GoSPanel.Service.exe') -ArgumentList 'legionspace-off' -Wait -WindowStyle Hidden
    & (Join-Path $PSScriptRoot 'install-widget.ps1')
    # Widget de Lenovo fuera de Game Bar, Game Bar reiniciado y agente en marcha (en la sesión del usuario, sin elevar)
    & (Join-Path $PSScriptRoot 'run-as-desktop-user.ps1') -Script (Join-Path $PSScriptRoot 'finish-install-user.ps1') | Out-Null
    Write-Host ''
    Write-Host 'GoPilot instalado. Abre Game Bar (Win + G o Legion R) > Widgets > GoPilot.' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host "No se pudo instalar GoPilot: $($_.Exception.Message)" -ForegroundColor Red
}
# Abierto con doble clic la ventana se cerraría sola: se espera para que se pueda leer el resultado
if ([Environment]::UserInteractive -and -not $env:SSH_CONNECTION) { Read-Host 'Pulsa Intro para cerrar' | Out-Null }
