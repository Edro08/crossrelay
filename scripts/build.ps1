Param(
    [ValidateSet("full", "win", "linux", "android")]
    [string]$Target = "full",
    [string]$AndroidTargets = "android/arm64,android/amd64",
    [switch]$SkipAndroidCore
)

$ErrorActionPreference = "Stop"
$RootDir = Split-Path -Parent $PSScriptRoot

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " CrossRelay Multiplatform Build Pipeline" -ForegroundColor Cyan
Write-Host " Target: $Target" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

function Build-Windows {
    Write-Host "`n[1/2] Building Go Core for Windows (win-x64)..." -ForegroundColor Yellow
    Push-Location "$RootDir\core"
    $env:GOOS = "windows"
    $env:GOARCH = "amd64"
    $env:CGO_ENABLED = "0"
    go build -ldflags="-s -w" -o "$RootDir\dist\win-x64\crossrelay-core.exe" ./cmd/main.go
    Pop-Location

    Write-Host "[2/2] Publishing Desktop UI for Windows (win-x64)..." -ForegroundColor Yellow
    Push-Location "$RootDir\desktop"
    dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -p:PublishAot=false -o "$RootDir\dist\win-x64"
    Pop-Location

    Write-Host "-> Windows build ready at dist/win-x64/" -ForegroundColor Green
}

function Build-Linux {
    $LinuxDist = "$RootDir\dist\linux-x64"
    New-Item -ItemType Directory -Force -Path $LinuxDist | Out-Null

    Write-Host "`n[1/3] Building Go Core for Linux (linux-x64)..." -ForegroundColor Yellow
    Push-Location "$RootDir\core"
    $env:GOOS = "linux"
    $env:GOARCH = "amd64"
    $env:CGO_ENABLED = "0"
    go build -ldflags="-s -w" -o "$LinuxDist\crossrelay-core" ./cmd/main.go
    Pop-Location

    Write-Host "[2/3] Publishing Desktop UI for Linux (linux-x64)..." -ForegroundColor Yellow
    Push-Location "$RootDir\desktop"
    dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=false -p:PublishAot=false -o $LinuxDist
    Pop-Location

    Write-Host "[3/3] Bundling Linux user installer (install.sh / uninstall.sh + icon)..." -ForegroundColor Yellow
    $IconSrc = "$RootDir\docs\assets\icono.png"
    if (Test-Path $IconSrc) {
        Copy-Item -Path $IconSrc -Destination "$LinuxDist\crossrelay.png" -Force
    }
    $Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    foreach ($scriptName in @("install.sh", "uninstall.sh")) {
        $srcScript = "$RootDir\scripts\linux\$scriptName"
        if (Test-Path $srcScript) {
            $lfContent = ([System.IO.File]::ReadAllText($srcScript)) -replace "`r`n", "`n"
            [System.IO.File]::WriteAllText("$LinuxDist\$scriptName", $lfContent, $Utf8NoBom)
        }
    }

    Write-Host "-> Linux build ready at dist/linux-x64/" -ForegroundColor Green
}

function Build-AndroidCore {
    $CoreDir = Join-Path $RootDir "core"
    $OutDir = Join-Path $RootDir "android\app\libs"
    $OutAar = Join-Path $OutDir "crossrelay.aar"

    $SdkDir = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA "Android\Sdk" }
    $NdkRoot = Join-Path $SdkDir "ndk"
    if (-not (Test-Path $NdkRoot)) {
        throw "Android NDK not found at '$NdkRoot'. Install 'NDK (Side by side)' in Android Studio SDK Manager -> SDK Tools."
    }

    $LatestNdk = Get-ChildItem $NdkRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $LatestNdk) {
        throw "No NDK version directory found inside '$NdkRoot'."
    }

    $env:ANDROID_HOME = $SdkDir
    $env:ANDROID_NDK_HOME = $LatestNdk.FullName
    $GoBin = Join-Path $env:USERPROFILE "go\bin"
    if (Test-Path $GoBin) {
        $env:PATH = "$GoBin;$env:PATH"
    }

    if (-not (Get-Command gomobile -ErrorAction SilentlyContinue)) {
        Write-Host "Installing gomobile..." -ForegroundColor Yellow
        go install golang.org/x/mobile/cmd/gomobile@latest
        go install golang.org/x/mobile/cmd/gobind@latest
    }

    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    Write-Host "`n[1/3] Building Go Mobile AAR ($AndroidTargets) using NDK $($LatestNdk.Name)..." -ForegroundColor Yellow
    Push-Location $CoreDir
    try {
        & gomobile bind -v "-target=$AndroidTargets" -androidapi 35 -o $OutAar ./mobile
        if ($LASTEXITCODE -ne 0) { throw "gomobile bind failed" }
    } finally {
        Pop-Location
    }
}

function Build-Android {
    $aarPath = "$RootDir\android\app\libs\crossrelay.aar"
    if (-not $SkipAndroidCore -or -not (Test-Path $aarPath)) {
        Build-AndroidCore
    } else {
        Write-Host "`n[1/3] Skipping Go Mobile AAR rebuild (using existing crossrelay.aar)" -ForegroundColor DarkGray
    }

    Write-Host "[2/3] Building signed Android Release APK (dev cert)..." -ForegroundColor Yellow
    $studioJbr = "C:\Program Files\Android\Android Studio\jbr"
    if ((-not $env:JAVA_HOME) -and (Test-Path $studioJbr)) {
        $env:JAVA_HOME = $studioJbr
    }
    Push-Location "$RootDir\android"
    try {
        & ".\gradlew.bat" assembleRelease
        if ($LASTEXITCODE -ne 0) { throw "Gradle assembleRelease failed" }
    } finally {
        Pop-Location
    }

    Write-Host "[3/3] Copying APK to dist/android/..." -ForegroundColor Yellow
    $releaseApk = "$RootDir\android\app\build\outputs\apk\release\app-release.apk"
    if (-not (Test-Path $releaseApk)) {
        throw "Release APK not found at $releaseApk"
    }
    $outDir = "$RootDir\dist\android"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    Copy-Item -Path $releaseApk -Destination "$outDir\CrossRelay-dev-release.apk" -Force
    Write-Host "-> Android Release APK ready at dist/android/CrossRelay-dev-release.apk" -ForegroundColor Green
}

switch ($Target) {
    "win"     { Build-Windows }
    "linux"   { Build-Linux }
    "android" { Build-Android }
    "full"    {
        Build-Windows
        Build-Linux
        Build-Android
    }
}

Write-Host "`nBuild complete successfully!" -ForegroundColor Green
