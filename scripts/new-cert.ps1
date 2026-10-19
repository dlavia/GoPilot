# Crea el certificado autofirmado con el que se firma el MSIX del widget.
# El sujeto debe coincidir con Publisher del Package.appxmanifest.
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\certs\GoSPanel.pfx'),
    [string]$Password = 'gospanel'
)

$ErrorActionPreference = 'Stop'
$cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=GoSPanel Dev' `
    -KeyUsage DigitalSignature -FriendlyName 'GoSPanel Dev' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') `
    -NotAfter (Get-Date).AddYears(5)

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$secure = ConvertTo-SecureString $Password -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $Out -Password $secure | Out-Null
Export-Certificate -Cert $cert -FilePath ([IO.Path]::ChangeExtension($Out, '.cer')) | Out-Null
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)"
Write-Host "Certificado creado: $Out (huella $($cert.Thumbprint))"
