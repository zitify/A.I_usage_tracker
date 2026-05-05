<#
.SYNOPSIS
    A.I. Usage Tracker MSIX 패키지 빌드 스크립트

.DESCRIPTION
    자체 서명 인증서 생성 → 앱 빌드 → MSIX 패키징 → 서명을 자동으로 수행합니다.

.EXAMPLE
    .\build-msix.ps1
#>

param(
    [string]$Configuration = "Release",
    [string]$Version = "2.13.24.0",
    [string]$OutputDir = "msix_output"
)

$ErrorActionPreference = "Stop"
$ProjectDir = $PSScriptRoot
$PublishDir = Join-Path $ProjectDir "publish_msix"
$MsixOutputDir = Join-Path $ProjectDir $OutputDir
$CertDir = Join-Path $ProjectDir "certs"
$CertPath = Join-Path $CertDir "AI_usage_tracker.pfx"
$CertPassword = if ($env:MSIX_CERT_PASSWORD) { $env:MSIX_CERT_PASSWORD } else {
    Read-Host -Prompt "인증서 비밀번호를 입력하세요" -AsSecureString |
        ForEach-Object { [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($_)) }
}
$PackageName = "AI_usage_tracker_v$($Version -replace '\.0$','')".TrimEnd('.')

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " A.I. Usage Tracker MSIX Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ─── Step 1: Find Windows SDK tools ───
Write-Host "[1/5] Windows SDK 도구 검색 중..." -ForegroundColor Yellow

$sdkPaths = @(
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
    "${env:ProgramFiles}\Windows Kits\10\bin"
)

$makeAppx = $null
$signTool = $null

foreach ($sdkBase in $sdkPaths) {
    if (Test-Path $sdkBase) {
        $versions = Get-ChildItem $sdkBase -Directory | Where-Object { $_.Name -match '^\d+\.' } | Sort-Object Name -Descending
        foreach ($ver in $versions) {
            $candidate = Join-Path $ver.FullName "x64\makeappx.exe"
            if (Test-Path $candidate) {
                $makeAppx = $candidate
                $signTool = Join-Path $ver.FullName "x64\signtool.exe"
                break
            }
        }
    }
    if ($makeAppx) { break }
}

if (-not $makeAppx) {
    Write-Host ""
    Write-Host "  [!] Windows SDK가 설치되어 있지 않습니다." -ForegroundColor Red
    Write-Host ""
    Write-Host "  다음 중 하나를 설치해주세요:" -ForegroundColor White
    Write-Host "    1. Visual Studio Installer → 개별 구성 요소 → 'Windows 10 SDK' 또는 'Windows 11 SDK'" -ForegroundColor Gray
    Write-Host "    2. winget install Microsoft.WindowsSDK.10.0.22621" -ForegroundColor Gray
    Write-Host "    3. https://developer.microsoft.com/windows/downloads/windows-sdk/" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  설치 후 이 스크립트를 다시 실행하세요." -ForegroundColor White
    exit 1
}

Write-Host "  makeappx: $makeAppx" -ForegroundColor Green
Write-Host "  signtool: $signTool" -ForegroundColor Green

# ─── Step 2: Create self-signed certificate ───
Write-Host ""
Write-Host "[2/5] 자체 서명 인증서 생성 중..." -ForegroundColor Yellow

if (-not (Test-Path $CertDir)) {
    New-Item -ItemType Directory -Path $CertDir -Force | Out-Null
}

if (Test-Path $CertPath) {
    Write-Host "  기존 인증서 사용: $CertPath" -ForegroundColor Green
} else {
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject "CN=zitify" `
        -KeyUsage DigitalSignature `
        -FriendlyName "A.I. Usage Tracker Signing Certificate" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

    $securePassword = ConvertTo-SecureString -String $CertPassword -Force -AsPlainText
    Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $CertPath -Password $securePassword | Out-Null

    Write-Host "  인증서 생성 완료: $CertPath" -ForegroundColor Green
    Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  [!] 사용자 PC에서 설치하려면 이 인증서를 '신뢰할 수 있는 루트 인증 기관'에 설치해야 합니다." -ForegroundColor DarkYellow
    Write-Host "      인증서 설치: certutil -addstore Root `"$CertPath`"" -ForegroundColor DarkYellow
}

# ─── Step 3: Publish the app ───
Write-Host ""
Write-Host "[3/5] 앱 빌드 및 퍼블리시 중..." -ForegroundColor Yellow

if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}

dotnet publish "$ProjectDir\AI_usage_tracker.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "  빌드 실패!" -ForegroundColor Red
    exit 1
}

Write-Host "  빌드 완료: $PublishDir" -ForegroundColor Green

# ─── Step 4: Prepare MSIX layout and build package ───
Write-Host ""
Write-Host "[4/5] MSIX 패키지 생성 중..." -ForegroundColor Yellow

# Copy AppxManifest to publish dir
Copy-Item (Join-Path $ProjectDir "Package.appxmanifest") (Join-Path $PublishDir "AppxManifest.xml") -Force

# Copy assets to publish dir
$assetsTarget = Join-Path $PublishDir "Assets"
if (-not (Test-Path $assetsTarget)) {
    New-Item -ItemType Directory -Path $assetsTarget -Force | Out-Null
}
Copy-Item (Join-Path $ProjectDir "Assets\Square44x44Logo.png") $assetsTarget -Force
Copy-Item (Join-Path $ProjectDir "Assets\Square150x150Logo.png") $assetsTarget -Force
Copy-Item (Join-Path $ProjectDir "Assets\StoreLogo.png") $assetsTarget -Force
Copy-Item (Join-Path $ProjectDir "Assets\Wide310x150Logo.png") $assetsTarget -Force

# Create output directory
if (-not (Test-Path $MsixOutputDir)) {
    New-Item -ItemType Directory -Path $MsixOutputDir -Force | Out-Null
}

$msixPath = Join-Path $MsixOutputDir "$PackageName.msix"

# Remove old package if exists
if (Test-Path $msixPath) {
    Remove-Item $msixPath -Force
}

# Build MSIX
& $makeAppx pack /d $PublishDir /p $msixPath /o

if ($LASTEXITCODE -ne 0) {
    Write-Host "  MSIX 패키징 실패!" -ForegroundColor Red
    exit 1
}

Write-Host "  패키지 생성: $msixPath" -ForegroundColor Green

# ─── Step 5: Sign the package ───
Write-Host ""
Write-Host "[5/5] MSIX 패키지 서명 중..." -ForegroundColor Yellow

& $signTool sign /fd SHA256 /a /f $CertPath /p $CertPassword $msixPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "  서명 실패!" -ForegroundColor Red
    exit 1
}

Write-Host "  서명 완료!" -ForegroundColor Green

# ─── Done ───
$fileSize = [math]::Round((Get-Item $msixPath).Length / 1MB, 2)
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " 빌드 완료!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  파일: $msixPath" -ForegroundColor White
Write-Host "  크기: ${fileSize} MB" -ForegroundColor White
Write-Host ""
Write-Host "  [설치 방법]" -ForegroundColor Yellow
Write-Host "  1. 인증서를 신뢰할 수 있는 루트에 설치 (최초 1회):" -ForegroundColor Gray
Write-Host "     powershell -Command `"Import-PfxCertificate -FilePath '$CertPath' -CertStoreLocation Cert:\LocalMachine\Root -Password (ConvertTo-SecureString '$CertPassword' -AsPlainText -Force)`"" -ForegroundColor DarkGray
Write-Host "  2. .msix 파일을 더블클릭하여 설치" -ForegroundColor Gray
Write-Host ""
