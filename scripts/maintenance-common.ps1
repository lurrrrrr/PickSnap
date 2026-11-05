function Assert-PickSnapPath([string]$Path, [string]$Root) {
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ($base.Length -lt 4 -or -not ($full.Equals($base, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($base + '\', [StringComparison]::OrdinalIgnoreCase))) { throw '文件路径超出允许的程序或数据目录。' }
    $current = $full
    while ($current -and $current.Length -ge $base.Length) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '目录包含链接，操作已停止以保护原文件。' }
        $current = Split-Path -Path $current -Parent
    }
}
function Assert-PickSnapTree([string]$Path, [string]$Root) {
    Assert-PickSnapPath $Path $Root
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $queue = New-Object 'Collections.Generic.Queue[string]'; $queue.Enqueue([IO.Path]::GetFullPath($Path))
    while ($queue.Count) {
        $next = $queue.Dequeue(); Assert-PickSnapPath $next $Root
        if ((Get-Item -LiteralPath $next -Force).PSIsContainer) {
            foreach ($child in Get-ChildItem -LiteralPath $next -Force) { Assert-PickSnapPath $child.FullName $Root; if ($child.PSIsContainer) { $queue.Enqueue($child.FullName) } }
        }
    }
}
function Move-PickSnapData([string]$DataPath, [string]$SourceRoot, [string]$CacheRoot) {
    Assert-PickSnapTree $DataPath $SourceRoot; Assert-PickSnapPath $CacheRoot $CacheRoot
    New-Item -ItemType Directory -Path $CacheRoot -Force | Out-Null
    $staged = Join-Path $CacheRoot 'Data'; $backup = $null
    if (Test-Path -LiteralPath $staged) {
        Assert-PickSnapTree $staged $CacheRoot
        $backup = Join-Path $CacheRoot ('Backups\Data-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
        Assert-PickSnapPath $backup $CacheRoot
        New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
        Move-Item -LiteralPath $staged -Destination $backup
    }
    try { Assert-PickSnapPath $staged $CacheRoot; Move-Item -LiteralPath $DataPath -Destination $staged -ErrorAction Stop }
    catch { if ($backup -and -not (Test-Path -LiteralPath $staged)) { Assert-PickSnapPath $backup $CacheRoot; Assert-PickSnapPath $staged $CacheRoot; Move-Item -LiteralPath $backup -Destination $staged }; throw }
    return $staged
}
