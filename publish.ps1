param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "publish",
    [string]$Channel = "win"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Join-Path $scriptDir "AutoFlow.App"
$publishDir = Join-Path $scriptDir $OutputDir

Write-Host "============================================"
Write-Host "AutoFlow Publish Script (Velopack)"
Write-Host "Configuration: $Configuration"
Write-Host "Runtime:       $Runtime"
Write-Host "Channel:       $Channel"
Write-Host "============================================"

Write-Host ""
Write-Host "[1/5] Installing vpk tool..."
$vpkToolsDir = Join-Path $scriptDir "vpk-tools"
if (Test-Path $vpkToolsDir) { Remove-Item -Recurse -Force $vpkToolsDir }
dotnet tool install vpk --tool-path $vpkToolsDir --version 0.0.1298 --framework net8.0
Write-Host "      Done."

Write-Host ""
Write-Host "[2/5] Cleaning previous output..."
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
Write-Host "      Done."

Write-Host ""
Write-Host "[3/5] Cleaning build artifacts..."
dotnet clean $projectDir -c $Configuration -r $Runtime --nologo -v q 2>&1 | Out-Null
$tempDir = Join-Path $projectDir ".temp"
if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue }
Write-Host "      Done."

Write-Host ""
Write-Host "[4/5] Publishing (SCD + SingleFile + ReadyToRun)..."
$publishArgs = @(
    "publish", $projectDir,
    "-c", $Configuration,
    "-r", $Runtime,
    "-o", $publishDir
)
$result = dotnet @publishArgs 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host $result
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $publishDir "AutoFlow.exe"
$version = (Get-Command $exePath -ErrorAction SilentlyContinue).FileVersionInfo.ProductVersion
if (-not $version) {
    $version = "0.0.1"
}
$version = $version -replace '\+.*$', ''
if ($version.StartsWith('0.0.0')) {
    $version = '0.0.1' + $version.Substring(5)
}
Write-Host "      Version: $version"
Write-Host "      Done."

Write-Host ""
Write-Host "[5/5] Creating Velopack release package..."
$vpkExe = Join-Path $vpkToolsDir "vpk.exe"
$vpkArgs = @(
    "pack",
    "--packId", "AutoFlow",
    "--packVersion", $version,
    "--packDir", $publishDir,
    "--outputDir", $publishDir,
    "--mainExe", "AutoFlow.exe",
    "--packTitle", "AutoFlow",
    "--channel", $Channel,
    "--noInst"
)
Write-Host "      Running: $vpkExe $($vpkArgs -join ' ')"
& $vpkExe @vpkArgs 2>&1 | ForEach-Object { Write-Host "      $_" }
if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed with exit code $LASTEXITCODE"
}

$releasesFile = Join-Path $publishDir "RELEASES"
$nupkgFiles = Get-ChildItem -Path $publishDir -Filter "AutoFlow-*.nupkg" -ErrorAction SilentlyContinue
$deltaFiles = Get-ChildItem -Path $publishDir -Filter "AutoFlow-*.delta" -ErrorAction SilentlyContinue

if (-not (Test-Path $releasesFile)) {
    throw "RELEASES file was not generated. Check vpk output above."
}
if (-not $nupkgFiles -or $nupkgFiles.Count -eq 0) {
    throw "No .nupkg file was generated. Check vpk output above."
}

Write-Host ""
Write-Host "      Generated files:"
Write-Host "        - RELEASES"
foreach ($file in $nupkgFiles) { Write-Host "        - $($file.Name)" }
foreach ($file in $deltaFiles) { Write-Host "        - $($file.Name)" }
Write-Host "      Done."

$renamedExe = "AutoFlow-v$version-$Runtime.exe"
$renamedExePath = Join-Path $publishDir $renamedExe
if (Test-Path $renamedExePath) { Remove-Item -Force $renamedExePath }
Rename-Item -Path $exePath -NewName $renamedExe
$exePath = $renamedExePath
Write-Host ""
Write-Host "      Renamed: AutoFlow.exe -> $renamedExe"

$zipName = "AutoFlow-v$version-$Runtime.zip"
$zipPath = Join-Path $publishDir $zipName
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path $exePath -DestinationPath $zipPath
Write-Host "      Created: $zipName"

$exeSize = 0
if (Test-Path $exePath) {
    $exeSize = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
}
$zipSize = 0
if (Test-Path $zipPath) {
    $zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
}

Write-Host ""
Write-Host "============================================"
Write-Host "  Publish completed successfully!"
Write-Host "============================================"
Write-Host "  Version : $version"
Write-Host "  EXE     : $exePath ($exeSize MB)"
Write-Host "  ZIP     : $zipPath ($zipSize MB)"
Write-Host "  Channel : $Channel"
Write-Host "============================================"
Write-Host ""
Write-Host "  Nupkg files ready in: $publishDir"
Write-Host "============================================"
