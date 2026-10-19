# Compila todo y deja en dist\ lo que hay que copiar a la Legion Go S:
#   dist\service\           servicio (lo instala install-service.ps1)
#   dist\widget\*.msix      widget firmado
#   dist\GoSPanel.cer       certificado para confiar en el MSIX
#   dist\*.ps1              scripts de instalación (install.ps1 hace todo) y desinstalación
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dist = Join-Path $root 'dist'
$pfx = Join-Path $root 'certs\GoSPanel.pfx'
if (-not (Test-Path $pfx)) { & (Join-Path $PSScriptRoot 'new-cert.ps1') }

# Vaciar (no borrar la carpeta, que puede estar abierta en una terminal). Si algo no se puede borrar se para:
# un widget viejo en dist\widget acabaría dentro de la versión publicada.
New-Item -ItemType Directory -Force $dist | Out-Null
Get-ChildItem $dist -Force | Remove-Item -Recurse -Force

dotnet publish (Join-Path $root 'src\GoSPanel.Service') -c $Configuration -o (Join-Path $dist 'service') -nologo
if ($LASTEXITCODE) { throw 'Fallo al publicar el servicio' }
Remove-Item (Join-Path $dist 'service\*.pdb')
# Firmado con el mismo certificado que el widget: apply-update.ps1 no instala un servicio con otra firma.
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" | Sort-Object FullName | Select-Object -Last 1
& $signtool.FullName sign /q /fd SHA256 /f $pfx /p gospanel (Join-Path $dist 'service\GoSPanel.Service.exe')
if ($LASTEXITCODE) { throw 'Fallo al firmar el servicio' }
# Los scripts de actualización y desinstalación viajan con el servicio (los ejecuta él).
'apply-update.ps1', 'uninstall.ps1' | ForEach-Object { Copy-Item (Join-Path $PSScriptRoot $_) (Join-Path $dist 'service') }

# Subir la revisión del paquete en cada compilación (Windows no reinstala la misma versión).
$manifest = Join-Path $root 'src\GoSPanel.Widget\Package.appxmanifest'
$xml = [xml](Get-Content $manifest -Raw)
$v = [version]$xml.Package.Identity.Version
$xml.Package.Identity.Version = "$($v.Major).$($v.Minor).$($v.Build).$($v.Revision + 1)"
$xml.Save($manifest)
Write-Host "Versión del paquete: $($xml.Package.Identity.Version)"

& (Join-Path $PSScriptRoot 'make-sounds.ps1')

# MSBuild de 64 bits: Smart App Control bloquea el genxbf.dll x86 del SDK (no está firmado) que carga el de 32.
$msbuild = &"${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
& $msbuild (Join-Path $root 'src\GoSPanel.Widget\GoSPanel.Widget.csproj') -restore -nologo -v:m `
    "-p:Configuration=$Configuration" -p:Platform=x64 `
    -p:GenerateAppxPackageOnBuild=true -p:UapAppxPackageBuildMode=SideloadOnly -p:AppxBundle=Never `
    -p:AppxPackageSigningEnabled=true "-p:PackageCertificateKeyFile=$pfx" -p:PackageCertificatePassword=gospanel `
    "-p:AppxPackageDir=$dist\widget\\"
if ($LASTEXITCODE) { throw 'Fallo al compilar el widget' }

Copy-Item ([IO.Path]::ChangeExtension($pfx, '.cer')) (Join-Path $dist 'GoSPanel.cer')
'Instalar GoPilot.cmd', 'install.ps1', 'install-service.ps1', 'update-service.ps1', 'install-widget.ps1', 'install-widget-user.ps1', 'run-as-desktop-user.ps1', 'finish-install-user.ps1', 'uninstall.ps1' |
    ForEach-Object { Copy-Item (Join-Path $PSScriptRoot $_) $dist }
Write-Host "Listo: $dist"
