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
Write-Host "[1/4] Cleaning previous output..."
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
Write-Host "      Done."

Write-Host ""
Write-Host "[2/4] Cleaning build artifacts..."
dotnet clean $projectDir -c $Configuration -r $Runtime --nologo -v q 2>&1 | Out-Null
$tempDir = Join-Path $projectDir "temp"
if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue }
Write-Host "      Done."

Write-Host ""
Write-Host "[3/4] Publishing (SCD + SingleFile + ReadyToRun)..."
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
Write-Host "      Done."

$publishName = "AutoFlow_$Runtime"

Write-Host ""
Write-Host "[4/4] Creating ZIP package..."
$zipFile = Join-Path $publishDir "$publishName.zip"
if (Test-Path $zipFile) {
    Remove-Item -Force $zipFile
}
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipFile
Write-Host "      Done."

$zipSize = [math]::Round((Get-Item $zipFile).Length / 1MB, 2)
$exeSize = 0
$exePath = Join-Path $publishDir "AutoFlow.exe"
if (Test-Path $exePath) {
    $exeSize = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
}

Write-Host ""
Write-Host "============================================"
Write-Host "  Publish completed successfully!"
Write-Host "============================================"
Write-Host "  EXE : $exePath ($exeSize MB)"
Write-Host "  ZIP : $zipFile ($zipSize MB)"
Write-Host "============================================"
