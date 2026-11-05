$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$verification = Join-Path $PSScriptRoot 'Verification'
$runtime = Join-Path $verification ('Runtime-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
foreach ($name in @('KingdomInc.exe','UnityPlayer.dll','UnityCrashHandler64.exe','winhttp.dll','doorstop_config.ini','.doorstop_version')) {
    New-Item -ItemType HardLink -Path (Join-Path $runtime $name) -Value (Join-Path $gameRoot $name) | Out-Null
}
foreach ($name in @('KingdomInc_Data','MonoBleedingEdge','D3D12')) {
    New-Item -ItemType Junction -Path (Join-Path $runtime $name) -Value (Join-Path $gameRoot $name) | Out-Null
}
$bep = Join-Path $runtime 'BepInEx'
New-Item -ItemType Directory -Path (Join-Path $bep 'plugins\PartyNode'),(Join-Path $bep 'config') -Force | Out-Null
New-Item -ItemType Junction -Path (Join-Path $bep 'core') -Value (Join-Path $gameRoot 'BepInEx\core') | Out-Null
Copy-Item -LiteralPath (Join-Path $gameRoot 'BepInEx\config\BepInEx.cfg') -Destination (Join-Path $bep 'config\BepInEx.cfg')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\Release\netstandard2.1\PartyNode.dll') -Destination (Join-Path $bep 'plugins\PartyNode\PartyNode.dll')
New-Item -ItemType Directory -Path (Join-Path $bep 'plugins\Foundation') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $gameRoot 'BepInEx\plugins\Foundation\Foundation.dll') -Destination (Join-Path $bep 'plugins\Foundation\Foundation.dll')
$logPath = Join-Path $runtime 'diagnostic.log'
$resultFolder = Join-Path $runtime '_Mods\PartyNode\Verification'
$resultPath = Join-Path $resultFolder 'runtime-tests.json'
$exe = Join-Path $runtime 'KingdomInc.exe'
$owned = Start-Process -FilePath $exe -WorkingDirectory $runtime -WindowStyle Hidden -ArgumentList @('-party-node-smoke','-screen-width','1280','-screen-height','900','-screen-fullscreen','0','-logFile',('"' + $logPath + '"')) -PassThru
try {
    $deadline = (Get-Date).AddSeconds(55)
    while ((Get-Date) -lt $deadline -and -not (Test-Path -LiteralPath $resultPath)) { Start-Sleep -Milliseconds 500 }
    if (-not (Test-Path -LiteralPath $resultPath)) {
        if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Tail 75 }
        throw "Isolated diagnostic did not complete. See $logPath"
    }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    foreach ($file in Get-ChildItem -LiteralPath $resultFolder -File) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $verification $file.Name) -Force }
    Copy-Item -LiteralPath $logPath -Destination (Join-Path $verification 'unity-isolated.log') -Force
    $result | ConvertTo-Json -Depth 8
    if (-not $result.passed) { throw "Isolated diagnostic failed. See $logPath" }
    Start-Sleep -Seconds 2
} finally {
    $owned.Refresh()
    if (-not $owned.HasExited) { Stop-Process -Id $owned.Id }
    foreach ($replacement in Get-CimInstance Win32_Process -Filter "Name='KingdomInc.exe'") {
        if ($replacement.ExecutablePath -eq $exe -and $replacement.CommandLine.Contains($logPath)) { Stop-Process -Id $replacement.ProcessId }
    }
}
