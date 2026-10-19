# Compila todo y deja en dist\ lo que hay que copiar a la Legion Go S:
#   dist\service\           servicio (lo instala install-service.ps1)
#   dist\widget\*.msix      widget firmado
#   dist\GoSPanel.cer       certificado para confiar en el MSIX
#   dist\*.ps1              scripts de instalación
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dist = Join-Path $root 'dist'
$pfx = Join-Path $root 'certs\GoSPanel.pfx'
if (-not (Test-Path $pfx)) { & (Join-Path $PSScriptRoot 'new-cert.ps1') }

Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist | Out-Null

dotnet publish (Join-Path $root 'src\GoSPanel.Service') -c $Configuration -o (Join-Path $dist 'service') -nologo
if ($LASTEXITCODE) { throw 'Fallo al publicar el servicio' }

# Subir la revisión del paquete en cada compilación (Windows no reinstala la misma versión).
$manifest = Join-Path $root 'src\GoSPanel.Widget\Package.appxmanifest'
$xml = [xml](Get-Content $manifest -Raw)
$v = [version]$xml.Package.Identity.Version
$xml.Package.Identity.Version = "$($v.Major).$($v.Minor).$($v.Build).$($v.Revision + 1)"
$xml.Save($manifest)
Write-Host "Versión del paquete: $($xml.Package.Identity.Version)"

& (Join-Path $PSScriptRoot 'make-sounds.ps1')

$msbuild = &"${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild (Join-Path $root 'src\GoSPanel.Widget\GoSPanel.Widget.csproj') -restore -nologo -v:m `
    "-p:Configuration=$Configuration" -p:Platform=x64 `
    -p:GenerateAppxPackageOnBuild=true -p:UapAppxPackageBuildMode=SideloadOnly -p:AppxBundle=Never `
    -p:AppxPackageSigningEnabled=true "-p:PackageCertificateKeyFile=$pfx" -p:PackageCertificatePassword=gospanel `
    "-p:AppxPackageDir=$dist\widget\\"
if ($LASTEXITCODE) { throw 'Fallo al compilar el widget' }

Copy-Item ([IO.Path]::ChangeExtension($pfx, '.cer')) (Join-Path $dist 'GoSPanel.cer')
'install-service.ps1', 'update-service.ps1', 'install-widget.ps1', 'install-widget-user.ps1', 'run-as-desktop-user.ps1' |
    ForEach-Object { Copy-Item (Join-Path $PSScriptRoot $_) $dist }
Write-Host "Listo: $dist"
