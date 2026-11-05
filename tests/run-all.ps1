$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $projectRoot 'scripts\build.ps1') -Package
& (Join-Path $PSScriptRoot 'run-safety.ps1')
& (Join-Path $PSScriptRoot 'run-motion.ps1')
& (Join-Path $PSScriptRoot 'run-features.ps1')
& (Join-Path $PSScriptRoot 'run-features.ps1') -Dark
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run-maintenance.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Windows PowerShell maintenance checks failed.' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $projectRoot 'build\PickSnap.exe')).ProductVersion
$archive = Join-Path $projectRoot ('releases\PickSnap-' + $version + '-windows-x64.zip')
& (Join-Path $projectRoot 'build\feature-tests\FeatureTests.exe') --verify-package $archive
if ($LASTEXITCODE -ne 0) { throw 'Actual release archive validation failed.' }
Write-Output 'All checks passed, including dark mode, Windows PowerShell 5.1, release archive validation.'
