param(
    [string]$Version = "v2.5.0"
)

$ErrorActionPreference = "Stop"
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Bakım - Windows x64 Tek Dosya Yayın Hazırlama Motoru" -ForegroundColor Green
Write-Host "  Hedef Sürüm: $Version" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

$root = $PSScriptRoot
if (-not $root) { $root = Get-Location }
$publishDir = Join-Path $root "bin\Publish\win-x64"
$releasesDir = Join-Path $root "Releases"

if (-not (Test-Path $releasesDir)) {
    New-Item -ItemType Directory -Path $releasesDir | Out-Null
}

Write-Host "`n[1/4] Derleme ve Tek Dosya (Self-Contained) paketleme başlatılıyor..." -ForegroundColor Cyan
$projFile = (Get-ChildItem -Path $root -Filter "*.csproj" | Select-Object -First 1).FullName
dotnet publish $projFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Derleme başarısız oldu!"
    exit 1
}

Write-Host "`n[2/4] Dağıtım dosyaları hazırlanıyor..." -ForegroundColor Cyan
$exeSource = Get-ChildItem -Path $publishDir -Filter "*.exe" | Select-Object -First 1
if (-not $exeSource) {
    Write-Error "Oluşturulan .exe dosyası bulunamadı!"
    exit 1
}

$destExeName = "Bakim.exe"
$destExePath = Join-Path $releasesDir $destExeName
Copy-Item -Path $exeSource.FullName -Destination $destExePath -Force

$zipPath = Join-Path $releasesDir "Bakim-$Version-win-x64.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Host "`n[3/4] ZIP Arşivi sıkıştırılıyor -> $zipPath" -ForegroundColor Cyan
Compress-Archive -Path $destExePath -DestinationPath $zipPath -Force

Write-Host "`n[4/4] SHA-256 Hash doğrulaması hesaplanıyor..." -ForegroundColor Cyan
$hash = (Get-FileHash -Path $destExePath -Algorithm SHA256).Hash
Write-Host "  Bakim.exe SHA-256: $hash" -ForegroundColor Yellow

$releaseUrl = "https://github.com/Eyupbayuk31/Bak-m-Arac-/releases/new"
Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "  YAYIN HAZIR! Dosyalarınız burada:" -ForegroundColor Green
Write-Host "  1. $destExePath" -ForegroundColor White
Write-Host "  2. $zipPath" -ForegroundColor White
Write-Host "`n  GitHub'a yüklemek için tarayıcı açılıyor:" -ForegroundColor Cyan
Write-Host "  $releaseUrl" -ForegroundColor Gray
Write-Host "==========================================================" -ForegroundColor Green

Start-Process $releaseUrl
explorer.exe $releasesDir
