$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Path "Certificates" -Force | Out-Null

Write-Host "Creating Code Signing Certificate for Bakım..."
$cert = New-SelfSignedCertificate `
    -Subject "CN=Bakım, O=Bakım Open Source Project, OU=Eyupbayuk31, C=TR" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -KeyUsage DigitalSignature `
    -Type CodeSigningCert `
    -NotAfter (Get-Date).AddYears(10)

$pwd = ConvertTo-SecureString -String "BakimSignKey2026!" -Force -AsPlainText

Export-PfxCertificate -Cert $cert -FilePath "Certificates\BakimCodeSigning.pfx" -Password $pwd | Out-Null
Export-Certificate -Cert $cert -FilePath "Certificates\BakimCodeSigning.cer" | Out-Null

Write-Host "CERTIFICATE GENERATED SUCCESSFULLY!"
Write-Host "Thumbprint: $($cert.Thumbprint)"
Write-Host "Subject:    $($cert.Subject)"
Write-Host "PFX Path:   Certificates\BakimCodeSigning.pfx"
Write-Host "CER Path:   Certificates\BakimCodeSigning.cer"
