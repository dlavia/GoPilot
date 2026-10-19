# Captura la pantalla principal en un PNG. Opcionalmente abre antes el widget como ventana normal.
param([string]$Out = 'C:\GoSPanel\shot.png', [switch]$LaunchWidget)

if ($LaunchWidget) {
    & (Join-Path $PSScriptRoot 'open-standalone.ps1')
    Start-Sleep -Seconds 8
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace Native -Name Dpi -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[Native.Dpi]::SetProcessDPIAware() | Out-Null

$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
"Captura: $Out ($($b.Width)x$($b.Height))"
