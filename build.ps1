<#
.SYNOPSIS
    BLivedmNotification 一键构建脚本
.DESCRIPTION
    构建 Python 后端 (PyInstaller) + C# 前端 (.NET Self-Contained)，
    组装到 dist/ 目录，可直接压缩分发。
.PARAMETER SkipPython
    跳过 Python 后端构建（仅构建 C# 前端）
.PARAMETER SkipDotnet
    跳过 .NET 前端构建（仅构建 Python 后端）
.PARAMETER Configuration
    .NET 构建配置，默认 Release
.PARAMETER Runtime
    .NET 目标运行时，默认 win-x64
.EXAMPLE
    .\build.ps1
    .\build.ps1 -SkipPython
    .\build.ps1 -SkipDotnet
#>
param(
    [switch]$SkipPython,
    [switch]$SkipDotnet,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$DistDir = Join-Path $ProjectRoot "dist"
$DistName = "BLivedmNotification"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  BLivedmNotification Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Project root: $ProjectRoot"
Write-Host "Output:       $DistDir\$DistName"
Write-Host ""

# ── Step 1: Build Python backend ──────────────────────────────────
if (-not $SkipPython) {
    Write-Host "[1/3] Building Python backend with PyInstaller..." -ForegroundColor Yellow

    # Check if pyinstaller is available
    $pyinstaller = $null
    if (Get-Command "pyinstaller" -ErrorAction SilentlyContinue) {
        $pyinstaller = "pyinstaller"
    }
    elseif (Test-Path (Join-Path $ProjectRoot ".venv\Scripts\pyinstaller.exe")) {
        $pyinstaller = Join-Path $ProjectRoot ".venv\Scripts\pyinstaller.exe"
    }
    else {
        Write-Host "PyInstaller not found. Installing..." -ForegroundColor Yellow

        # Try uv first, then pip
        $uv = Get-Command "uv" -ErrorAction SilentlyContinue
        if ($uv) {
            & uv pip install pyinstaller
        }
        else {
            $venvPip = Join-Path $ProjectRoot ".venv\Scripts\pip.exe"
            if (Test-Path $venvPip) {
                & $venvPip install pyinstaller
            }
            else {
                pip install pyinstaller
            }
        }

        if (Get-Command "pyinstaller" -ErrorAction SilentlyContinue) {
            $pyinstaller = "pyinstaller"
        }
        elseif (Test-Path (Join-Path $ProjectRoot ".venv\Scripts\pyinstaller.exe")) {
            $pyinstaller = Join-Path $ProjectRoot ".venv\Scripts\pyinstaller.exe"
        }
        else {
            Write-Host "[ERR] Failed to install PyInstaller" -ForegroundColor Red
            exit 1
        }
    }

    Write-Host "Using PyInstaller: $pyinstaller"

    # Clean previous build
    $buildDir = Join-Path $ProjectRoot "build"
    $pyDistDir = Join-Path $ProjectRoot "dist_pyinstaller"
    if (Test-Path $buildDir) { Remove-Item $buildDir -Recurse -Force }
    if (Test-Path $pyDistDir) { Remove-Item $pyDistDir -Recurse -Force }

    # Build using spec file
    & $pyinstaller "blivedm_backend.spec" `
        --distpath $pyDistDir `
        --workpath $buildDir `
        --noconfirm

    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERR] PyInstaller build failed" -ForegroundColor Red
        exit 1
    }

    $backendExe = Join-Path $pyDistDir "blivedm_backend.exe"
    if (-not (Test-Path $backendExe)) {
        Write-Host "[ERR] Backend exe not found at $backendExe" -ForegroundColor Red
        exit 1
    }

    $sizeMb = [math]::Round((Get-Item $backendExe).Length / 1MB, 1)
    Write-Host "[OK] Backend exe built: $sizeMb MB" -ForegroundColor Green

    # Cleanup build artifacts
    if (Test-Path $buildDir) { Remove-Item $buildDir -Recurse -Force }
}
else {
    Write-Host "[1/3] Skipping Python backend build (-SkipPython)" -ForegroundColor Gray
}

# ── Step 2: Build .NET frontend ──────────────────────────────────
if (-not $SkipDotnet) {
    Write-Host ""
    Write-Host "[2/3] Building .NET frontend (Self-Contained $Runtime)..." -ForegroundColor Yellow

    $csprojDir = Join-Path $ProjectRoot "overlay-csharp\WpfOverlay"

    # Clean previous publish
    $publishDir = Join-Path $csprojDir "bin\$Configuration\net8.0-windows\$Runtime\publish"
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    & dotnet publish $csprojDir `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false

    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERR] dotnet publish failed" -ForegroundColor Red
        Write-Host "Make sure .NET 8 SDK is installed: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
        exit 1
    }

    Write-Host "[OK] .NET frontend published" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "[2/3] Skipping .NET frontend build (-SkipDotnet)" -ForegroundColor Gray
}

# ── Step 3: Assemble distribution ─────────────────────────────────
Write-Host ""
Write-Host "[3/3] Assembling distribution..." -ForegroundColor Yellow

# Create dist directory
$distOutput = Join-Path $DistDir $DistName
if (Test-Path $distOutput) { Remove-Item $distOutput -Recurse -Force }
New-Item -ItemType Directory -Path $distOutput -Force | Out-Null

# Copy .NET publish output
if (-not $SkipDotnet) {
    $csprojDir = Join-Path $ProjectRoot "overlay-csharp\WpfOverlay"
    $publishDir = Join-Path $csprojDir "bin\$Configuration\net8.0-windows\$Runtime\publish"

    if (Test-Path $publishDir) {
        Copy-Item "$publishDir\*" $distOutput -Recurse -Force
        Write-Host "  Copied .NET publish output"
    }
}

# Copy Python backend exe
if (-not $SkipPython) {
    $pyDistDir = Join-Path $ProjectRoot "dist_pyinstaller"
    $backendExe = Join-Path $pyDistDir "blivedm_backend.exe"

    if (Test-Path $backendExe) {
        Copy-Item $backendExe $distOutput -Force
        Write-Host "  Copied blivedm_backend.exe"
    }
}

# Copy config files
$configJson = Join-Path $ProjectRoot "config.json"
$configExample = Join-Path $ProjectRoot "config.example.json"
if (Test-Path $configJson) {
    Copy-Item $configJson $distOutput -Force
    Write-Host "  Copied config.json"
}
if (Test-Path $configExample) {
    Copy-Item $configExample $distOutput -Force
    Write-Host "  Copied config.example.json"
}

# Copy icon
$icon = Join-Path $ProjectRoot "icon.ico"
if (Test-Path $icon) {
    Copy-Item $icon $distOutput -Force
    Write-Host "  Copied icon.ico"
}

# Cleanup Python build artifacts
$pyDistDir = Join-Path $ProjectRoot "dist_pyinstaller"
if (Test-Path $pyDistDir) { Remove-Item $pyDistDir -Recurse -Force }

# Calculate total size
$totalSize = (Get-ChildItem $distOutput -Recurse | Measure-Object -Property Length -Sum).Sum
$totalSizeMb = [math]::Round($totalSize / 1MB, 1)
$fileCount = (Get-ChildItem $distOutput -Recurse -File).Count

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Output:  $distOutput"
Write-Host "Size:    $totalSizeMb MB ($fileCount files)"
Write-Host ""
Write-Host "To distribute:" -ForegroundColor Cyan
Write-Host "  1. Zip the '$DistName' folder"
Write-Host "  2. Users extract and run WpfOverlay.exe"
Write-Host "  3. No Python or .NET installation required"
