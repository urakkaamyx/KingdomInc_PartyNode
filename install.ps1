$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (Get-CimInstance Win32_Process -Filter "Name='KingdomInc.exe'" | Where-Object { $_.ExecutablePath -eq (Join-Path $gameRoot 'KingdomInc.exe') }) {
    throw 'Close Kingdom .Inc before installing Party-Up.'
}
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'BepInEx\plugins\Foundation\Foundation.dll'))) {
    throw 'Install Foundation 0.5.0 or later before installing Party-Up.'
}
dotnet build (Join-Path $PSScriptRoot 'PartyNode.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Party Node build failed.' }
$destination = Join-Path $gameRoot 'BepInEx\plugins\PartyNode'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$dllPath = Join-Path $destination 'PartyNode.dll'
if (Test-Path -LiteralPath $dllPath) {
    $backup = Join-Path $PSScriptRoot ('_Backup\' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    Copy-Item -LiteralPath $dllPath -Destination (Join-Path $backup 'PartyNode.dll')
}
$builtDll = Join-Path $PSScriptRoot 'bin\Release\netstandard2.1\PartyNode.dll'
try { Copy-Item -LiteralPath $builtDll -Destination $dllPath -Force -ErrorAction Stop }
catch [System.IO.IOException] {
    if (-not (Test-Path -LiteralPath $dllPath)) { throw }
    # Windows can keep a running assembly mapped. Rename it into the verified
    # project backup, then install the next-launch DLL without stopping the game.
    $resolvedDll = (Resolve-Path -LiteralPath $dllPath).Path
    $resolvedBackup = (Resolve-Path -LiteralPath $backup).Path
    if (-not $resolvedDll.StartsWith($gameRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        -not $resolvedBackup.StartsWith($gameRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Install paths escaped game root.' }
    Move-Item -LiteralPath $resolvedDll -Destination (Join-Path $resolvedBackup 'PartyNode.loaded.dll') -ErrorAction Stop
    Copy-Item -LiteralPath $builtDll -Destination $dllPath -ErrorAction Stop
}
Write-Output "Installed: $dllPath"
Write-Output 'Party Node will load the next time Kingdom Inc starts.'
