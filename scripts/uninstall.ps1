param(
    [ValidateSet('Ask', 'KeepData', 'RemoveAll')][string]$Mode = 'Ask'
)
$ErrorActionPreference = 'Stop'

# 拾帧卸载程序：先确认，再停程序、清快捷方式与注册表、清缓存，最后删文件。
# 原则：默认保留用户收集的图片与设置（data 目录），因为删掉无法恢复；
# 想彻底清空必须显式选择「不保留」。

Add-Type -AssemblyName System.Windows.Forms | Out-Null
$installPath = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\PickSnap'))
$dataPath = Join-Path $installPath 'data'
$exePath = Join-Path $installPath 'PickSnap.exe'
$cacheRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PickSnap'

function Show-Info([string]$text, [string]$icon = 'Information') {
    [System.Windows.Forms.MessageBox]::Show($text, '拾帧 PickSnap', 'OK', $icon) | Out-Null
}

# 便携运行（没走 Install.cmd）时没有安装目录，不碰注册表和快捷方式，直接说明怎么移除。
if (-not [string]::Equals([IO.Path]::GetFullPath($PSScriptRoot), $installPath, [StringComparison]::OrdinalIgnoreCase)) {
    Show-Info ("当前不是通过 Install.cmd 安装的拾帧，没有注册表项和快捷方式需要卸载。`n`n直接删除程序所在文件夹即可彻底移除。`n`n截图与设置就在该文件夹的 data 子目录里，需要保留的话请先备份。")
    exit 0
}

if (-not (Test-Path -LiteralPath $installPath)) {
    Show-Info '没有找到拾帧的安装目录，可能已经卸载过了。'
    exit 0
}
if ((Get-Item -LiteralPath $installPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    Show-Info '安装目录是目录链接，为避免误删已停止卸载，请手动处理。'
    exit 1
}

# 第一步：确认要卸载。
if ($Mode -eq 'Ask') {
    $answer = [System.Windows.Forms.MessageBox]::Show(
        ("确定要卸载拾帧 PickSnap 吗？`n`n程序文件位于：`n$installPath`n`n下一步会让你选择是否保留已收集的图片和设置。"),
        '卸载拾帧', 'YesNo', 'Question', 'Button2')
    if ($answer -ne 'Yes') { exit 0 }
}

# 第二步：决定 data 的去留。默认按钮是「是」，即保留。
$keepData = $Mode -ne 'RemoveAll'
if ($Mode -eq 'Ask') {
    $answer = [System.Windows.Forms.MessageBox]::Show(
        ("是否保留已收集的图片和设置？`n`n是：只删除程序，图片与设置留在 data 目录，重新安装后继续可用（推荐）`n否：连同图片和设置一起删除，无法恢复`n取消：中止卸载"),
        '图片与设置', 'YesNoCancel', 'Warning', 'Button1')
    if ($answer -eq 'Cancel') { exit 0 }
    $keepData = $answer -eq 'Yes'
}

# 先让程序正常退出：走 --quit，给它 10 秒；退不掉就停下来，避免删到一半留下半残状态。
$running = @(Get-CimInstance Win32_Process -Filter "Name = 'PickSnap.exe'" |
    Where-Object { $_.ExecutablePath -eq $exePath })
if ($running.Count) {
    if (Test-Path -LiteralPath $exePath) {
        Start-Process -FilePath $exePath -ArgumentList '--quit' -WindowStyle Hidden -Wait -ErrorAction SilentlyContinue
    }
    foreach ($process in $running) {
        $active = Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue
        if ($active -and -not $active.WaitForExit(10000)) {
            Show-Info '拾帧仍在运行，卸载已停止。请先在托盘退出拾帧（或处理未保存的图片）后重试。' 'Warning'
            exit 1
        }
    }
}
Start-Sleep -Milliseconds 300

# 桌面快捷方式：只删指向本安装目录的那一个，别误伤同名的其他快捷方式。
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PickSnap.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    try {
        $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
        if ($shortcut.TargetPath -eq $exePath) { Remove-Item -LiteralPath $shortcutPath -Force }
    }
    catch { }
}

# 注册表：开机启动项与「设置 → 应用」里的卸载条目。
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PickSnap' -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PickSnap' -Recurse -Force -ErrorAction SilentlyContinue

# 更新缓存（下载过的安装包，纯缓存，不属于用户数据），无论保留与否都清掉。
foreach ($name in @('Updates')) {
    $path = Join-Path $cacheRoot $name
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Remove-Everything([string]$path) {
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        try {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
            return $true
        }
        catch { Start-Sleep -Milliseconds 350 }
    }
    return $false
}

# 删文件：逐个顶层项处理并重试，某个文件被占用时记下来，最后统一告知，而不是中途中断。
$failed = New-Object 'System.Collections.Generic.List[string]'
foreach ($child in @(Get-ChildItem -LiteralPath $installPath -Force)) {
    if ($keepData -and $child.Name -ieq 'data') { continue }
    if (-not (Remove-Everything $child.FullName)) { $failed.Add($child.Name) }
}

$pictureCount = 0
if ($keepData -and (Test-Path -LiteralPath $dataPath)) {
    # data 里唯一可以丢的是编辑器的 WebView2 缓存和错误日志，删掉能省几十 MB，重新打开会自动重建。
    foreach ($name in @('EditorCache', 'error.log')) {
        $path = Join-Path $dataPath $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue }
    }
    $captures = Join-Path $dataPath 'Captures'
    if (Test-Path -LiteralPath $captures) {
        $pictureCount = @(Get-ChildItem -LiteralPath $captures -File -ErrorAction SilentlyContinue).Count
    }
}

# 目录空了就连同外层缓存目录一起收掉；保留数据时目录里还有 data，不能删。
$leftover = @(Get-ChildItem -LiteralPath $installPath -Force -ErrorAction SilentlyContinue).Count
if ($leftover -eq 0 -and $failed.Count -eq 0) {
    Remove-Item -LiteralPath $installPath -Recurse -Force -ErrorAction SilentlyContinue
    if ((Test-Path -LiteralPath $cacheRoot) -and @(Get-ChildItem -LiteralPath $cacheRoot -Force -ErrorAction SilentlyContinue).Count -eq 0) {
        Remove-Item -LiteralPath $cacheRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($failed.Count -gt 0) {
    Show-Info ("拾帧已卸载，但下面这些文件没能删除（可能正被其他程序占用）：`n`n" + ($failed -join "`n") + "`n`n稍后手动删除这个目录即可：`n$installPath") 'Warning'
}
elseif ($keepData) {
    Show-Info ("拾帧已卸载。`n`n图片与设置已保留（共 $pictureCount 张截图）：`n$dataPath`n`n重新运行 Install.cmd 安装后会自动继续使用这些内容。`n如果不再需要，直接删除上面这个目录即可。")
}
else {
    Show-Info '拾帧已卸载，程序、图片、设置和缓存都已删除。'
}
exit 0
