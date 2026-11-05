param([ValidateSet('Ask', 'KeepData', 'RemoveAll')][string]$Mode = 'Ask', [switch]$PlanOnly)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'maintenance-common.ps1')
$appRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$standardRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\PickSnap'))
$cacheRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PickSnap'))
$dataPath = Join-Path $appRoot 'data'; $exePath = Join-Path $appRoot 'PickSnap.exe'
$installed = $appRoot.Equals($standardRoot, [StringComparison]::OrdinalIgnoreCase)
if ($PlanOnly) { [PSCustomObject]@{ Program=$appRoot; Data=$dataPath; Installed=$installed; Default='KeepData'; ExistingBackups='Preserve'; ChangesApplied=$false } | ConvertTo-Json; exit 0 }
# Windows 应用列表与直接运行脚本，优先进入同一个 PickSnap 卸载选择页。
# 只有确认后的 KeepData/RemoveAll 进入下方实际文件操作，避免重复确认。
if ($Mode -eq 'Ask' -and (Test-Path -LiteralPath $exePath)) {
    try {
        $dialogProcess = Start-Process -FilePath $exePath -ArgumentList '--uninstall' -WindowStyle Hidden -PassThru -Wait
        if ($dialogProcess.ExitCode -eq 0) { exit 0 }
    } catch { } # 程序本体无法运行时，仍保留脚本对话框作为修复入口。
}
Add-Type -AssemblyName PresentationFramework
function Show-Result([string]$text, [string]$icon = 'Information') { [Windows.MessageBox]::Show($text, '卸载 PickSnap', 'OK', $icon) | Out-Null }
try {
    if (-not (Test-Path -LiteralPath $exePath)) { throw '找不到程序文件，请从完整安装目录运行卸载。' }
    Assert-PickSnapTree $appRoot $appRoot
    if ($Mode -eq 'Ask') {
        $choice = [Windows.MessageBox]::Show("卸载 PickSnap 并保留图片与设置？`n`n是：卸载，保留当前数据（推荐）`n否：卸载，并删除当前数据`n取消：不作任何更改`n`n已有的其他暂存和备份不会删除。", '卸载 PickSnap', 'YesNoCancel', 'Question', 'Cancel')
        if ($choice -eq 'Cancel') { exit 0 }
        $Mode = if ($choice -eq 'Yes') { 'KeepData' } else { 'RemoveAll' }
        if ($Mode -eq 'RemoveAll' -and [Windows.MessageBox]::Show("确认永久删除当前数据？`n$dataPath`n`n图片、文字和设置无法恢复。", '确认删除当前数据', 'YesNo', 'Warning', 'No') -ne 'Yes') { exit 0 }
    }
    $running = @(Get-CimInstance Win32_Process -Filter "Name = 'PickSnap.exe'" | Where-Object { $_.ExecutablePath -eq $exePath })
    if ($running.Count) {
        Start-Process -FilePath $exePath -ArgumentList '--quit' -WindowStyle Hidden -Wait
        foreach ($process in $running) { $active = Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue; if ($active -and -not $active.WaitForExit(10000)) { throw 'PickSnap 仍在运行。请先保存图片并退出，再重新卸载。' } }
    }
    $preserved = $null
    if ($Mode -eq 'KeepData' -and (Test-Path -LiteralPath $dataPath)) { $preserved = Move-PickSnapData $dataPath $appRoot $cacheRoot }
    if ($Mode -eq 'RemoveAll' -and (Test-Path -LiteralPath $dataPath)) { Assert-PickSnapTree $dataPath $appRoot; Remove-Item -LiteralPath $dataPath -Recurse -Force -ErrorAction Stop }
    if ($installed) {
        foreach ($child in @(Get-ChildItem -LiteralPath $appRoot -Force)) { Assert-PickSnapTree $child.FullName $appRoot; Remove-Item -LiteralPath $child.FullName -Recurse -Force -ErrorAction Stop }
        Assert-PickSnapPath $appRoot $standardRoot; Remove-Item -LiteralPath $appRoot -Force -ErrorAction Stop
    }
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PickSnap.lnk'
    if (Test-Path -LiteralPath $shortcutPath) { $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath); if ($shortcut.TargetPath -eq $exePath) { Remove-Item -LiteralPath $shortcutPath -Force } }
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $run = Get-ItemProperty -LiteralPath $runKey -Name PickSnap -ErrorAction SilentlyContinue
    if ($run -and $run.PickSnap -eq ('"' + $exePath + '"')) { Remove-ItemProperty -LiteralPath $runKey -Name PickSnap }
    $keyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PickSnap'
    $registered = Get-ItemProperty -LiteralPath $keyPath -ErrorAction SilentlyContinue
    if ($installed -and $registered -and $registered.InstallLocation -eq $appRoot) { Remove-Item -LiteralPath $keyPath -Recurse -Force }
    $note = if ($preserved) { "图片、文字与设置已保留到：`n$preserved`n`n如原先已有暂存，已另外保存在 Backups 中。" } elseif ($Mode -eq 'RemoveAll') { '当前数据已删除，其他暂存和备份没有改动。' } else { '当前没有需要处理的数据，其他暂存与备份没有改动。' }
    if ($installed) { Show-Result ("程序已卸载。`n`n" + $note) } else { Show-Result ("PickSnap 已退出。请删除以下文件夹完成移除：`n$appRoot`n`n" + $note) }
}
catch { Show-Result ("卸载未完成：" + $_.Exception.Message + "`n`n请保留当前目录和数据，修复后重试。") 'Warning'; exit 1 }
