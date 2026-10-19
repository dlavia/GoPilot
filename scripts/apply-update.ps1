# Instala una versión nueva de GoPilot descargada por el servicio (Updater.cs). Va junto al servicio y lo
# ejecuta una tarea programada como SYSTEM: para el servicio, lo sustituye y actualiza el widget del usuario.
# -Source: carpeta con el contenido del .zip publicado (service\, widget\*.msix).
param([Parameter(Mandatory)][string]$Source)

$ErrorActionPreference = 'Stop'
$folder = Join-Path $env:ProgramData 'GoSPanel\update'
$status = Join-Path $folder 'status.json'
$target = Join-Path $env:ProgramFiles 'GoSPanel\Service'
$version = Split-Path $Source -Leaf

function Write-Status([string]$state, [string]$message) {
    [ordered]@{ state = $state; version = $version; message = $message; time = (Get-Date).ToString('s') } |
        ConvertTo-Json | Set-Content $status -Encoding UTF8
}

# Ejecuta un comando de PowerShell en la sesión del usuario (Add-AppxPackage instala por usuario) y espera.
function Invoke-AsDesktopUser([string]$command) {
    $user = (Get-CimInstance Win32_ComputerSystem).UserName
    if (-not $user) { throw 'No hay ningún usuario con sesión iniciada' }
    # La carpeta solo la pueden modificar SYSTEM y administradores: el registro se crea aquí y se le da permiso al
    # usuario solo sobre ese archivo (si no puede abrirlo, PowerShell aborta el comando antes de empezar).
    $log = Join-Path $folder 'user-step.log'
    Set-Content $log '' -Encoding UTF8
    icacls.exe $log /grant "$($user):(M)" | Out-Null
    $taskName = 'GoSPanel-UpdateUser'
    # conhost --headless: sin ventana que robe el foco al juego
    $action = New-ScheduledTaskAction -Execute 'conhost.exe' `
        -Argument "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command `"& { $command } *> '$log'`""
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Force | Out-Null
    try {
        Start-ScheduledTask -TaskName $taskName
        # Add-AppxPackage puede tardar varios minutos si la Tienda está instalando otras actualizaciones
        $deadline = (Get-Date).AddMinutes(15)
        do { Start-Sleep -Seconds 1 } while ((Get-ScheduledTask -TaskName $taskName).State -in 'Queued', 'Running' -and (Get-Date) -lt $deadline)
        if (Test-Path $log) { Get-Content $log }
    }
    finally {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }
}

Start-Transcript (Join-Path $folder 'update.log') -Force | Out-Null
try {
    Write-Status 'installing' 'Comprobando la firma'
    # Solo se instala lo firmado con el certificado de GoPilot en el que confió la instalación original.
    $pinned = @(Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=GoSPanel Dev' | ForEach-Object Thumbprint)
    if (-not $pinned) { throw 'No se encuentra el certificado de GoPilot en el equipo' }
    $msix = Get-ChildItem (Join-Path $Source 'widget') -Recurse -Filter "*_$($version)_*.msix" | Select-Object -First 1
    if (-not $msix) { throw "El paquete no contiene el widget $version" }
    foreach ($file in $msix.FullName, (Join-Path $Source 'service\GoSPanel.Service.exe')) {
        $signature = Get-AuthenticodeSignature $file
        # UnknownError = firma íntegra pero el certificado no es de una entidad raíz (es propio); HashMismatch o
        # NotSigned = archivo modificado o sin firmar.
        if ($signature.Status -notin 'Valid', 'UnknownError' -or $signature.SignerCertificate.Thumbprint -notin $pinned) {
            throw "Firma no válida en $(Split-Path $file -Leaf) ($($signature.Status))"
        }
    }

    Write-Status 'installing' 'Actualizando el servicio'
    $backup = "$target.bak"
    Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue
    Stop-Service GoSPanel -Force
    (Get-Service GoSPanel).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
    Get-Process GoSPanel.Service -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    Copy-Item $target $backup -Recurse
    try {
        Copy-Item (Join-Path $Source 'service\*') $target -Recurse -Force
        Start-Service GoSPanel
    }
    catch {
        # Si algo falla, se vuelve al servicio anterior
        Copy-Item (Join-Path $backup '*') $target -Recurse -Force
        Start-Service GoSPanel
        throw
    }

    Write-Status 'installing' 'Actualizando el widget'
    $deps = (Get-ChildItem (Join-Path $msix.DirectoryName 'Dependencies\x64') -Filter '*.appx' -ErrorAction SilentlyContinue |
        ForEach-Object { "'$($_.FullName)'" }) -join ','
    # Las dependencias (VCLibs, .NET Native) solo si faltan: pasarlas siempre hace que Windows las trate como una
    # actualización del framework y reinicie todas las apps que las usan (Xbox, Game Bar…): minutos en vez de segundos.
    $add = "Add-AppxPackage -Path '$($msix.FullName)' -ForceApplicationShutdown"
    $install = if ($deps) { "try { $add -ErrorAction Stop } catch { $add -DependencyPath $deps }" } else { $add }
    # Tras instalar: reiniciar Game Bar (si no, no muestra el widget nuevo) y volver a arrancar el agente.
    Invoke-AsDesktopUser ((
        $install,
        'Get-Process GameBar, GameBarFTServer, XboxGameBarWidgets, GoSPanel.Widget -ErrorAction SilentlyContinue | Stop-Process -Force',
        '$p = Get-AppxPackage GoSPanel',
        "Invoke-CommandInDesktopPackage -PackageFamilyName `$p.PackageFamilyName -AppId App -Command (Join-Path `$p.InstallLocation 'Bridge\GoSPanel.Bridge.exe') -Args 'relay'"
    ) -join '; ')
    $installed = (Get-AppxPackage -AllUsers GoSPanel | Select-Object -First 1).Version
    if ($installed -ne $version) { throw "El widget no se actualizó (instalada: $installed)" }

    Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $Source -Recurse -Force -ErrorAction SilentlyContinue
    Write-Status 'done' "Actualizado a $version"
}
catch {
    Write-Status 'error' $_.Exception.Message
    throw
}
finally {
    Stop-Transcript | Out-Null
    Unregister-ScheduledTask -TaskName 'GoSPanel-Update' -Confirm:$false -ErrorAction SilentlyContinue
}
