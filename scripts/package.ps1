param([string]$ExecutablePath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'build\PickSnap.exe'))
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$stagePath = [IO.Path]::GetFullPath((Join-Path $projectRoot 'build\package'))
if (-not $stagePath.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '打包目录超出项目范围' }
if (Test-Path -LiteralPath $stagePath) { Remove-Item -LiteralPath $stagePath -Recurse -Force }
$appPath = Join-Path $stagePath 'app'
New-Item -ItemType Directory -Path $appPath -Force | Out-Null
$files = [ordered]@{
    'PickSnap.exe.config' = 'src\App.config'
    'Microsoft.Web.WebView2.Core.dll' = 'vendor\WebView2\Microsoft.Web.WebView2.Core.dll'
    'Microsoft.Web.WebView2.Wpf.dll' = 'vendor\WebView2\Microsoft.Web.WebView2.Wpf.dll'
    'WebView2Loader.dll' = 'vendor\WebView2\WebView2Loader.dll'
    'LICENSE' = 'LICENSE'
    'NOTICE' = 'NOTICE'
    'uninstall.ps1' = 'scripts\uninstall.ps1'
    'update.ps1' = 'scripts\update.ps1'
    'editor\index.html' = 'src\editor\index.html'
    'editor\editor.js' = 'src\editor\editor.js'
    'editor\editor.css' = 'src\editor\editor.css'
    'editor\translations.js' = 'src\editor\translations.js'
    'editor\filerobot-image-editor.min.js' = 'vendor\Filerobot\filerobot-image-editor.min.js'
    'third-party-notices\Filerobot\LICENSE' = 'vendor\Filerobot\LICENSE'
    'third-party-notices\Filerobot\THIRD-PARTY-NOTICES.txt' = 'vendor\Filerobot\THIRD-PARTY-NOTICES.txt'
    'third-party-notices\WebView2\LICENSE.txt' = 'vendor\WebView2\LICENSE.txt'
    'third-party-notices\WebView2\NOTICE.txt' = 'vendor\WebView2\NOTICE.txt'
}
Copy-Item -LiteralPath $ExecutablePath -Destination (Join-Path $appPath 'PickSnap.exe')
foreach ($file in $files.GetEnumerator()) {
    $destination = Join-Path $appPath $file.Key
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot $file.Value) -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.cmd'),(Join-Path $PSScriptRoot 'install.ps1') -Destination $stagePath
$releasePath = Join-Path $projectRoot 'releases'
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo([IO.Path]::GetFullPath($ExecutablePath)).ProductVersion
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw '程序版本号无效' }
$archivePath = Join-Path $releasePath ('PickSnap-' + $version + '-windows-x64.zip')
Compress-Archive -LiteralPath $appPath,(Join-Path $stagePath 'Install.cmd'),(Join-Path $stagePath 'install.ps1') -DestinationPath $archivePath -Force
$checksum = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archivePath)
Set-Content -LiteralPath ($archivePath + '.sha256') -Value $checksum -Encoding ASCII
Write-Output ('发布包：' + $archivePath)
