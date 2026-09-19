param(
    [string]$Version = "v3.0.1"
)

$ErrorActionPreference = "Stop"
$cleanVersion = $Version.TrimStart('v', 'V')

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Bakım - Windows x64 Yayın ve Kurulum (Installer) Motoru" -ForegroundColor Green
Write-Host "  Hedef Sürüm: $Version (Temiz: $cleanVersion)" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan

$root = $PSScriptRoot
if (-not $root) { $root = Get-Location }
$publishDir = Join-Path $root "bin\Publish\win-x64"
$releasesDir = Join-Path $root "Releases"

if (-not (Test-Path $releasesDir)) {
    New-Item -ItemType Directory -Path $releasesDir | Out-Null
}

Write-Host "`n[1/5] Derleme ve Tek Dosya (Self-Contained) paketleme başlatılıyor..." -ForegroundColor Cyan
$projFile = (Get-ChildItem -Path $root -Filter "*.csproj" | Select-Object -First 1).FullName
dotnet publish $projFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Derleme başarısız oldu!"
    exit 1
}

Write-Host "`n[2/5] Dağıtım dosyaları hazırlanıyor..." -ForegroundColor Cyan
$exeSource = Get-ChildItem -Path $publishDir -Filter "*.exe" | Select-Object -First 1
if (-not $exeSource) {
    Write-Error "Oluşturulan .exe dosyası bulunamadı!"
    exit 1
}

$destExeName = "Bakim.exe"
$destExePath = Join-Path $releasesDir $destExeName
Copy-Item -Path $exeSource.FullName -Destination $destExePath -Force

$zipPath = Join-Path $releasesDir "Bakim-v$cleanVersion-win-x64.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Host "`n[3/5] Portable ZIP Arşivi sıkıştırılıyor -> $zipPath" -ForegroundColor Cyan
Compress-Archive -Path $destExePath -DestinationPath $zipPath -Force

Write-Host "`n[4/5] Inno Setup ile Profesyonel Windows Kurulum Dosyası (.exe) derleniyor..." -ForegroundColor Cyan
$isccCandidates = @(
    "C:\Users\eyup9\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)

$isccPath = $null
foreach ($c in $isccCandidates) {
    if (Test-Path $c) {
        $isccPath = $c
        break
    }
}

if (-not $isccPath) {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { $isccPath = $cmd.Source }
}

$setupExePath = Join-Path $releasesDir "Bakim-v$cleanVersion-Setup.exe"

if ($isccPath) {
    $issFile = Join-Path $root "Bakim_Setup.iss"
    Write-Host "  ISCC bulundu: $isccPath" -ForegroundColor Gray
    & "$isccPath" "/DMyAppVersion=$cleanVersion" "$issFile"
    if ($LASTEXITCODE -eq 0 -and (Test-Path $setupExePath)) {
        Write-Host "  [BAŞARILI] Kurulum dosyası üretildi: $setupExePath" -ForegroundColor Green
    } else {
        Write-Warning "Inno Setup derlemesi hata verdi veya dosya bulunamadı."
    }
} else {
    Write-Warning "Inno Setup (ISCC.exe) bulunamadı. Kurulum oluşturma adımı atlandı."
}

Write-Host "`n[5/5] SHA-256 Hash doğrulamaları hesaplanıyor..." -ForegroundColor Cyan
if (Test-Path $destExePath) {
    $hashExe = (Get-FileHash -Path $destExePath -Algorithm SHA256).Hash
    Write-Host "  Bakim.exe SHA-256:       $hashExe" -ForegroundColor Yellow
}
if (Test-Path $setupExePath) {
    $hashSetup = (Get-FileHash -Path $setupExePath -Algorithm SHA256).Hash
    Write-Host "  Bakim-Setup.exe SHA-256: $hashSetup" -ForegroundColor Yellow
}

$releaseUrl = "https://github.com/Eyupbayuk31/Bakim/releases/new"
Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "  TÜM YAYIN DOSYALARI HAZIR! (Releases/ klasörü):" -ForegroundColor Green
if (Test-Path $setupExePath) { Write-Host "  1. [KURULUM] $setupExePath" -ForegroundColor White }
if (Test-Path $destExePath)  { Write-Host "  2. [TEK EXE] $destExePath" -ForegroundColor White }
if (Test-Path $zipPath)      { Write-Host "  3. [ZIP ARŞİV] $zipPath" -ForegroundColor White }
Write-Host "`n  GitHub'a yüklemek için tarayıcı açılıyor:" -ForegroundColor Cyan
Write-Host "  $releaseUrl" -ForegroundColor Gray
Write-Host "==========================================================" -ForegroundColor Green

Start-Process $releaseUrl
explorer.exe $releasesDir
