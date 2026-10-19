# Parte de usuario de la instalación del widget (Add-AppxPackage). El certificado lo importa install-widget.ps1.
$msix = Get-ChildItem (Join-Path $PSScriptRoot 'widget') -Recurse -Filter '*.msix' | Select-Object -First 1
Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown
Get-AppxPackage GoSPanel | Select-Object Name, Version, Status | Format-List

# Tras actualizar, Game Bar no vuelve a mostrar el widget (o lo muestra vacío) hasta que se reinician Game Bar,
# su host de widgets y el propio widget; se relanzan solos al abrir Game Bar.
Get-Process GameBar, GameBarFTServer, XboxGameBarWidgets, GoSPanel.Widget -ErrorAction SilentlyContinue | Stop-Process -Force
