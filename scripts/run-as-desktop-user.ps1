# Ejecuta un script en la sesión interactiva del usuario (necesario para Add-AppxPackage,
# capturas de pantalla, etc. cuando se trabaja por SSH). Espera a que termine y muestra su salida.
param(
    [Parameter(Mandatory)][string]$Script,
    [string]$Arguments = '',
    [string]$User = $env:USERNAME,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
$log = Join-Path $env:TEMP "gospanel-task-$([guid]::NewGuid().ToString('N')).log"
$taskName = 'GoSPanel-DesktopRun'

# conhost --headless: sin ventana. Con Windows Terminal como consola predeterminada, -WindowStyle Hidden
# abre igualmente una ventana que roba el foco al juego (y los juegos a pantalla completa se minimizan).
$action = New-ScheduledTaskAction -Execute 'conhost.exe' `
    -Argument "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command `"& '$Script' $Arguments *> '$log'`""
$principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive -RunLevel Limited
# También con batería: por defecto Windows deja la tarea «en cola» sin cargador (una consola casi siempre)
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null

try {
    Start-ScheduledTask -TaskName $taskName
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    # La tarea puede pasar unos segundos «en cola» antes de arrancar: esperar a que empiece y termine.
    do { Start-Sleep -Milliseconds 500 } while ((Get-ScheduledTask -TaskName $taskName).State -in 'Queued', 'Running' -and (Get-Date) -lt $deadline)
    if (Test-Path $log) { Get-Content $log; Remove-Item $log }
}
finally {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}
