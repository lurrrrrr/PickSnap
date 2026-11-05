$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $projectRoot 'scripts\maintenance-common.ps1')
$testRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('PickSnap-maintenance-' + [Guid]::NewGuid().ToString('N'))))
$passed = 0
function Check([bool]$value, [string]$message) { if (-not $value) { throw $message } }
function Reject([scriptblock]$action, [string]$message) { $rejected = $false; try { & $action } catch { $rejected = $true }; Check $rejected $message }
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$junction = Join-Path $testRoot 'source\link'
try {
    $source = Join-Path $testRoot 'source'; $cache = Join-Path $testRoot 'cache'; $current = Join-Path $source 'data'; $staged = Join-Path $cache 'Data'
    New-Item -ItemType Directory -Path $current,$staged -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $current 'new.txt') -Value 'new data'; Set-Content -LiteralPath (Join-Path $staged 'old.txt') -Value 'older data'
    $moved = Move-PickSnapData $current $source $cache
    Check (Test-Path -LiteralPath (Join-Path $moved 'new.txt')) 'Current data was not staged'
    $backups = @(Get-ChildItem -LiteralPath (Join-Path $cache 'Backups') -Directory)
    Check ($backups.Count -eq 1 -and (Test-Path -LiteralPath (Join-Path $backups[0].FullName 'old.txt'))) 'Existing staged data was overwritten'
    $passed++; Write-Output 'PASS: Existing staged data is retained as a separate backup'
    Reject { Assert-PickSnapPath (Join-Path $testRoot 'outside.txt') $source } 'Sibling path accepted'
    Reject { Assert-PickSnapPath $source ([IO.Path]::GetPathRoot($source)) } 'Drive-root mutation accepted'
    $passed++; Write-Output 'PASS: Sibling paths and volume roots are rejected'
    Reject { Move-PickSnapData (Join-Path $source 'missing-data') $source $cache } 'Missing-source move unexpectedly succeeded'
    Check (Test-Path -LiteralPath (Join-Path $cache 'Data\new.txt')) 'Failed staging did not restore the previous canonical data'
    $passed++; Write-Output 'PASS: Failed data staging restores the previous staged folder'
    $outside = Join-Path $testRoot 'outside'; New-Item -ItemType Directory -Path $outside -Force | Out-Null; Set-Content -LiteralPath (Join-Path $outside 'sentinel.txt') -Value 'keep'
    New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    Reject { Assert-PickSnapTree $source $source } 'Directory junction accepted'
    Check (Test-Path -LiteralPath (Join-Path $outside 'sentinel.txt')) 'External data changed'
    $passed++; Write-Output 'PASS: Linked trees are rejected without changing their targets'
    # 只加载更新器的两个函数，在临时夹具内验证真实文件替换，不运行安装/重启主流程。
    $updateAst = [Management.Automation.Language.Parser]::ParseFile((Join-Path $projectRoot 'scripts\update.ps1'), [ref]$null, [ref]$null)
    foreach ($name in @('Assert-PlainPath','Copy-UpdateFile')) {
        $functionAst = $updateAst.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
        Check ($null -ne $functionAst) ('Missing updater function ' + $name)
        . ([scriptblock]::Create($functionAst.Extent.Text))
    }
    $targetRoot = Join-Path $testRoot 'target'; $workRoot = $cache
    New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
    $payload = Join-Path $testRoot 'payload.bin'; $destination = Join-Path $targetRoot 'program.bin'
    [IO.File]::WriteAllText($payload, 'new payload'); [IO.File]::WriteAllText($destination, 'old payload')
    $locked = [IO.File]::Open($destination, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { Reject { Copy-UpdateFile $payload $destination } 'Locked destination was replaced' } finally { $locked.Dispose() }
    Check ([IO.File]::ReadAllText($destination) -eq 'old payload') 'Failed update truncated the old program'
    Copy-UpdateFile $payload $destination
    Check ([IO.File]::ReadAllText($destination) -eq 'new payload') 'Unlocked update did not replace the program'
    Check (@(Get-ChildItem -LiteralPath $targetRoot -Filter '*.tmp').Count -eq 0) 'Updater left a temporary file'
    $passed++; Write-Output 'PASS: Atomic update preserves locked originals and replaces unlocked files'
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'scripts') -Filter '*.ps1') {
        $parseErrors = $null; $tokens = $null
        [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null
        Check ($parseErrors.Count -eq 0) ('PowerShell syntax error in ' + $file.Name)
    }
    $passed++; Write-Output 'PASS: All maintenance/build scripts parse successfully'
}
finally {
    if (Test-Path -LiteralPath $junction) {
        $fullLink = [IO.Path]::GetFullPath($junction)
        Check ($fullLink.StartsWith($testRoot + '\', [StringComparison]::OrdinalIgnoreCase)) 'Unsafe link cleanup path'
        Check (((Get-Item -LiteralPath $junction -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) 'Cleanup target is not a link'
        [IO.Directory]::Delete($junction)
    }
    Assert-PickSnapTree $testRoot ([IO.Path]::GetTempPath().TrimEnd('\'))
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
Write-Output ($passed.ToString() + ' maintenance checks passed; no real installation was changed.')
