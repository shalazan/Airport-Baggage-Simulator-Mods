# PowerShell script to build and package mods for release
$ErrorActionPreference = "Stop"

$workspaceRoot = $PSScriptRoot
$releasesDir = Join-Path $workspaceRoot "releases"
$tempDir = Join-Path $workspaceRoot "temp_release_build"

# List of mod projects to build
$mods = @("BaggageTagAnyMod", "CounterSorterMod", "InnovationLevelMod")

# Ensure clean releases directory
if (Test-Path $releasesDir) {
    Remove-Item -Recurse -Force $releasesDir
}
New-Item -ItemType Directory -Path $releasesDir | Out-Null

Write-Host "Starting build and release packaging..." -ForegroundColor Cyan

foreach ($mod in $mods) {
    $projectPath = Join-Path $workspaceRoot $mod
    $csprojFile = Join-Path $projectPath "$mod.csproj"
    
    if (-not (Test-Path $csprojFile)) {
        Write-Warning "Project file not found: $csprojFile"
        continue
    }

    # Parse version from .csproj
    [xml]$xml = Get-Content $csprojFile
    $version = $xml.Project.PropertyGroup.Version
    if (-not $version) {
        $version = "1.0.0"
    }

    Write-Host "----------------------------------------" -ForegroundColor Gray
    Write-Host "Building $mod (v$version)..." -ForegroundColor Yellow

    # Run dotnet build in Release configuration
    dotnet build $csprojFile -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build project: $mod"
    }

    # Locate compiled DLL
    $binDir = Join-Path $projectPath "bin\Release\net472"
    $dllFile = Join-Path $binDir "$mod.dll"

    if (-not (Test-Path $dllFile)) {
        # Fallback check if it was compiled with standard TargetFramework net472 or standard folder structure
        $dllFile = Get-ChildItem -Path $projectPath -Filter "$mod.dll" -Recurse | Where-Object { $_.FullName -like "*Release*" } | Select-Object -ExpandProperty FullName -First 1
    }

    if (-not $dllFile -or -not (Test-Path $dllFile)) {
        throw "Could not find built DLL for $mod in bin folder."
    }

    Write-Host "Packaging $mod..." -ForegroundColor Green

    # Setup temp workspace for packaging
    if (Test-Path $tempDir) {
        Remove-Item -Recurse -Force $tempDir
    }
    New-Item -ItemType Directory -Path $tempDir | Out-Null

    # Copy DLL to temp directory
    Copy-Item $dllFile -Destination $tempDir

    # Create ZIP archive
    $zipPath = Join-Path $releasesDir "$mod-v$version.zip"
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Compress-Archive -Path "$tempDir\*" -DestinationPath $zipPath -Force

    # Clean up temp
    Remove-Item -Recurse -Force $tempDir

    Write-Host "Successfully packaged: $zipPath" -ForegroundColor Green
}

Write-Host "----------------------------------------" -ForegroundColor Gray
Write-Host "All releases built and packaged successfully inside $releasesDir!" -ForegroundColor Green
