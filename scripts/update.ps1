param([Parameter(Mandatory=$true)][string]$WorkPath, [Parameter(Mandatory=$true)][string]$TargetPath, [int]$ParentProcessId)
$ErrorActionPreference = 'Stop'
$cacheRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PickSnap\Updates'))
$workRoot = [IO.Path]::GetFullPath($WorkPath).TrimEnd('\')
$targetRoot = [IO.Path]::GetFullPath($TargetPath).TrimEnd('\')
$sourceRoot = Join-Path $workRoot 'contents\app'
$exePath = Join-Path $targetRoot 'PickSnap.exe'
$backupRoot = Join-Path $workRoot 'backup'
$changed = New-Object 'Collections.Generic.List[object]'
$mayRestart = $false
function Copy-UpdateFile([string]$SourceFile, [string]$DestinationFile) {
    $allowed = if ($DestinationFile.StartsWith($targetRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { $targetRoot } else { $workRoot }
    Assert-PlainPath $DestinationFile $allowed
    $temporary = $DestinationFile + '.update-' + [Guid]::NewGuid().ToString('N') + '.tmp'
    Assert-PlainPath $temporary $allowed
    try {
        for ($attempt = 0; ; $attempt++) {
            try {
                $inputStream = [IO.File]::OpenRead($SourceFile)
                try { $outputStream = [IO.File]::Create($temporary); try { $inputStream.CopyTo($outputStream); $outputStream.Flush($true) } finally { $outputStream.Dispose() } }
                finally { $inputStream.Dispose() }
                if (Test-Path -LiteralPath $DestinationFile) { [IO.File]::Replace($temporary, $DestinationFile, [NullString]::Value) } else { [IO.File]::Move($temporary, $DestinationFile) }
                return
            }
            catch [IO.IOException] {
                $cause = $_.Exception; while ($cause.InnerException) { $cause = $cause.InnerException }
                $code = $cause.HResult -band 65535
                if ($attempt -ge 4 -or $code -notin @(32,33,1175)) { throw }
                Start-Sleep -Milliseconds 80
            }
        }
    }
    finally { if (Test-Path -LiteralPath $temporary) { Assert-PlainPath $temporary $allowed; Remove-Item -LiteralPath $temporary -Force } }
}
function Assert-PlainPath([string]$PathToCheck, [string]$AllowedRoot) {
    $resolved = [IO.Path]::GetFullPath($PathToCheck)
    if (-not ($resolved -eq $AllowedRoot -or $resolved.StartsWith($AllowedRoot + '\', [StringComparison]::OrdinalIgnoreCase))) { throw '更新文件超出安装目录。' }
    $ancestor = $resolved
    while ($ancestor -and $ancestor.Length -ge $AllowedRoot.Length) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '更新目录不能包含目录链接。' }
        $ancestor = Split-Path -Path $ancestor -Parent
    }
}
try {
    if (-not $workRoot.StartsWith($cacheRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw '更新缓存目录无效。' }
    Assert-PlainPath $workRoot $cacheRoot
    if (-not (Test-Path -LiteralPath $exePath) -or -not (Test-Path -LiteralPath (Join-Path $sourceRoot 'PickSnap.exe'))) { throw '更新文件不完整。' }
    Assert-PlainPath $targetRoot $targetRoot
    if ($ParentProcessId -gt 0) {
        $parent = Get-Process -Id $ParentProcessId -ErrorAction SilentlyContinue
        if ($parent -and -not $parent.WaitForExit(30000)) { throw 'PickSnap 尚未退出，请先保存图片并关闭程序。' }
    }
    # 完整检查所有路径后才开始替换，避免半途才发现包内路径或目录链接异常。
    foreach ($candidate in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
        $relative = $candidate.FullName.Substring($sourceRoot.Length + 1)
        if ($relative -match '^(data|test-data|preview-data)(\\|$)') { throw '更新包不能覆盖用户图片和设置。' }
        Assert-PlainPath $candidate.FullName $sourceRoot
        Assert-PlainPath (Join-Path $targetRoot $relative) $targetRoot
    }
    $mayRestart = $true
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Sort-Object FullName) {
        $relative = $file.FullName.Substring($sourceRoot.Length + 1)
        if ($relative -match '^(data|test-data)(\\|$)') { throw '更新包不能覆盖用户图片和设置。' }
        Assert-PlainPath $file.FullName $sourceRoot
        $destination = Join-Path $targetRoot $relative
        Assert-PlainPath $destination $targetRoot
        $backup = Join-Path $backupRoot $relative
        $existed = Test-Path -LiteralPath $destination
        if ($existed) {
            New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
            Copy-UpdateFile $destination $backup
        }
        $changed.Add([PSCustomObject]@{ Destination=$destination; Backup=$backup; Existed=$existed })
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-UpdateFile $file.FullName $destination
    }
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PickSnap'
    $registered = Get-ItemProperty -LiteralPath $uninstallKey -ErrorAction SilentlyContinue
    if ($registered -and [string]::Equals($registered.InstallLocation, $targetRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion
        Set-ItemProperty -LiteralPath $uninstallKey -Name DisplayVersion -Value $version
    }
    Start-Process -FilePath $exePath -WorkingDirectory $targetRoot -WindowStyle Hidden
    try { Assert-PlainPath $workRoot $cacheRoot; Remove-Item -LiteralPath $workRoot -Recurse -Force } catch { }
}
catch {
    $message = $_.Exception.Message
    Write-Error -Message ($message + "`r`n" + $_.InvocationInfo.PositionMessage) -ErrorAction Continue
    $restored = $true
    foreach ($file in $changed) {
        try {
            Assert-PlainPath $file.Destination $targetRoot
            if ($file.Existed) { Copy-UpdateFile $file.Backup $file.Destination }
            elseif (Test-Path -LiteralPath $file.Destination) { Remove-Item -LiteralPath $file.Destination -Force }
        }
        catch { $restored = $false }
    }
    if ($mayRestart -and $restored) { Start-Process -FilePath $exePath -WorkingDirectory $targetRoot -WindowStyle Hidden }
    Add-Type -AssemblyName System.Windows.Forms
    $recovery = if ($restored) { '原版本已保留，可稍后重试。' } else { '请重新运行安装包修复程序，图片和设置仍保留在 data 文件夹。' }
    [Windows.Forms.MessageBox]::Show(('更新未完成：' + $message + "`r`n" + $recovery), 'PickSnap 更新', 'OK', 'Warning') | Out-Null
    exit 1
}
