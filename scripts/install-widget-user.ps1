# Parte de usuario de la instalación del widget (Add-AppxPackage). El certificado lo importa install-widget.ps1.
# Antes se cierra todo lo del paquete: si Windows lo encuentra abierto, al terminar lo «reinicia» como aplicación normal
# (ventana a pantalla completa). Después Game Bar vuelve a mostrar el widget nuevo al abrirse.
Get-Process GameBar, GameBarFTServer, XboxGameBarWidgets, GoSPanel.Widget, GoSPanel.Bridge -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
$msix = Get-ChildItem (Join-Path $PSScriptRoot 'widget') -Recurse -Filter '*.msix' | Select-Object -First 1
Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown
Get-AppxPackage GoSPanel | Select-Object Name, Version, Status | Format-List
