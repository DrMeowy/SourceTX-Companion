# SourceTX Companion App - PowerShell Build Script
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "  SourceTX Companion App - Build Script (.NET/WPF)" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host ""

$projectDir = $PSScriptRoot
$csproj = Join-Path $projectDir "SourceTXCompanion.csproj"
$msbuild = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"

if (-not (Test-Path $msbuild)) {
    Write-Host "[ERROR] MSBuild not found at $msbuild" -ForegroundColor Red
    exit 1
}

Write-Host "[BUILD] Building SourceTX Companion (Release)..." -ForegroundColor Yellow
& $msbuild $csproj /p:Configuration=Release /p:Platform=AnyCPU /verbosity:minimal /nologo

$outputExe = Join-Path $projectDir "bin\Release\SourceTXCompanion.exe"
if (Test-Path $outputExe) {
    $rootExe = Join-Path $projectDir "SourceTXCompanion.exe"
    try {
        Copy-Item -Path $outputExe -Destination $rootExe -Force -ErrorAction Stop
    } catch {
        Write-Host "[NOTE] Root SourceTXCompanion.exe is currently open. Close it to update root copy." -ForegroundColor Yellow
    }

    $fileInfo = Get-Item $outputExe
    Write-Host ""
    Write-Host "===================================================" -ForegroundColor Green
    Write-Host "  [SUCCESS] Build Succeeded!" -ForegroundColor Green
    Write-Host "  Binary (Release) : $($outputExe)" -ForegroundColor White
    Write-Host "  Size             : $([math]::Round($fileInfo.Length / 1KB, 1)) KB" -ForegroundColor White
    Write-Host "===================================================" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Binary not found after build." -ForegroundColor Red
    exit 1
}
