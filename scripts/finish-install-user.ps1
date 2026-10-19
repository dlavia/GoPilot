# Última parte de install.ps1, en la sesión del usuario y sin elevar (la lanza run-as-desktop-user.ps1):
# quita de Game Bar el widget de Legion Space (GoPilot ocupa su sitio), reinicia Game Bar y arranca el agente para que
# el widget y los botones Legion respondan ya, sin esperar al próximo inicio de sesión.
Get-AppxPackage LegionGoGameBarWidget | Remove-AppxPackage -ErrorAction SilentlyContinue
Get-Process GameBar, GameBarFTServer, XboxGameBarWidgets, GoSPanel.Widget -ErrorAction SilentlyContinue | Stop-Process -Force
if (-not (Get-Process GoSPanel.Bridge -ErrorAction SilentlyContinue)) {
    $package = Get-AppxPackage GoSPanel
    Invoke-CommandInDesktopPackage -PackageFamilyName $package.PackageFamilyName -AppId App `
        -Command (Join-Path $package.InstallLocation 'Bridge\GoSPanel.Bridge.exe') -Args 'relay'
}
