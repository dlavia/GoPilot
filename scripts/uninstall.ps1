# Desinstala GoPilot por completo. Lo lanza el servicio (Updater.Uninstall) como SYSTEM desde una copia temporal;
# también se puede ejecutar a mano como administrador. Deja Legion Space activado, como estaba antes de GoPilot.
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Continue'
Start-Transcript (Join-Path $env:TEMP 'gopilot-uninstall.log') -Force | Out-Null

# 1. Widget del usuario con sesión iniciada (los paquetes se quitan por usuario, desde su sesión)
$user = (Get-CimInstance Win32_ComputerSystem).UserName
if ($user -and [Security.Principal.WindowsIdentity]::GetCurrent().IsSystem) {
    $taskName = 'GoSPanel-UninstallUser'
    $action = New-ScheduledTaskAction -Execute 'conhost.exe' `
        -Argument "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command `"Get-AppxPackage GoSPanel | Remove-AppxPackage; if (-not (Get-AppxPackage LegionGoGameBarWidget)) { Add-AppxPackage -RegisterByFamilyName -MainPackage LegionGoGameBarWidget_ra2g5j82mn5h8 -ErrorAction SilentlyContinue }; Get-Process GameBar, GameBarFTServer, XboxGameBarWidgets -ErrorAction SilentlyContinue | Stop-Process -Force`""
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName
    $deadline = (Get-Date).AddMinutes(5)
    do { Start-Sleep -Seconds 1 } while ((Get-ScheduledTask -TaskName $taskName).State -in 'Queued', 'Running' -and (Get-Date) -lt $deadline)
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}
else {
    Get-AppxPackage GoSPanel | Remove-AppxPackage
}
# Cualquier otro usuario que lo tuviera instalado
Get-AppxPackage -AllUsers GoSPanel | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue

# 2. Legion Space: volver a arrancarlo con Windows si GoPilot lo había desactivado
if (Get-Service DAService -ErrorAction SilentlyContinue) {
    sc.exe config DAService start= auto | Out-Null
    Start-Service DAService -ErrorAction SilentlyContinue
}

# 3. Hueco del fabricante en Game Bar: devolverlo a Lenovo si aún apunta a GoPilot
$key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\GamingConfiguration'
$state = Join-Path $env:ProgramData 'GoSPanel\state.json'
if ((Get-ItemProperty $key -ErrorAction SilentlyContinue).OEMGameBarwidget -eq 'GoSPanel_3ncfs0ed3kzx8') {
    $original = $null
    if (Test-Path $state) { $original = (Get-Content $state -Raw | ConvertFrom-Json).oemWidgetOriginal }
    if ($original) { Set-ItemProperty $key OEMGameBarwidget $original }
    else { Remove-ItemProperty $key OEMGameBarwidget }
}

# 4. Servicio, tareas, archivos y certificado
Stop-Service GoSPanel -Force -ErrorAction SilentlyContinue
Get-Process GoSPanel.Service -ErrorAction SilentlyContinue | Stop-Process -Force
sc.exe delete GoSPanel | Out-Null
Get-ScheduledTask -TaskName 'GoSPanel-*' -ErrorAction SilentlyContinue |
    Where-Object TaskName -ne 'GoSPanel-Uninstall' | Unregister-ScheduledTask -Confirm:$false
Start-Sleep -Seconds 1
Remove-Item (Join-Path $env:ProgramFiles 'GoSPanel') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:ProgramData 'GoSPanel') -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=GoSPanel Dev' | Remove-Item

# Game Bar vuelve a leer el hueco del fabricante al abrirse
Get-Process GameBar, GameBarFTServer -ErrorAction SilentlyContinue | Stop-Process -Force

Stop-Transcript | Out-Null
Unregister-ScheduledTask -TaskName 'GoSPanel-Uninstall' -Confirm:$false -ErrorAction SilentlyContinue
Remove-Item $PSCommandPath -Force -ErrorAction SilentlyContinue
