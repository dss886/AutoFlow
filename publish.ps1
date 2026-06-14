param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "publish"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Join-Path $scriptDir "AutoFlow.App"
$publishDir = Join-Path $scriptDir $OutputDir

Write-Host "============================================"
Write-Host "AutoFlow Publish Script"
Write-Host "Configuration: $Configuration"
Write-Host "Runtime:       $Runtime"
Write-Host "============================================"

Write-Host ""
Write-Host "[1/3] Cleaning previous output..."
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
Write-Host "      Done."

Write-Host ""
Write-Host "[2/3] Cleaning build artifacts..."
dotnet clean $projectDir -c $Configuration -r $Runtime --nologo -v q 2>&1 | Out-Null
$tempDir = Join-Path $projectDir ".temp"
if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue }
Write-Host "      Done."

Write-Host ""
Write-Host "[3/3] Publishing (SCD + SingleFile + ReadyToRun)..."
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
Write-Host "============================================"
