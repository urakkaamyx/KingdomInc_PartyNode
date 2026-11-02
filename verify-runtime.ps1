$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (Get-Process -Name KingdomInc -ErrorAction SilentlyContinue) { throw 'Close Kingdom Inc before running the isolated diagnostic.' }
$verification = Join-Path $PSScriptRoot 'Verification'
New-Item -ItemType Directory -Path $verification -Force | Out-Null
$logPath = Join-Path $verification ('unity-' + [Guid]::NewGuid().ToString('N') + '.log')
$resultPath = Join-Path $verification 'runtime-tests.json'
$startedAt = Get-Date
$gameExe = Join-Path $gameRoot 'KingdomInc.exe'
$ownedProcess = Start-Process -FilePath $gameExe -WorkingDirectory $gameRoot -WindowStyle Hidden -ArgumentList @('-party-node-smoke', '-screen-width', '1280', '-screen-height', '900', '-screen-fullscreen', '0', '-logFile', ('"' + $logPath + '"')) -PassThru
try {
    $deadline = (Get-Date).AddSeconds(55)
    $result = $null
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path -LiteralPath $resultPath) -and (Get-Item -LiteralPath $resultPath).LastWriteTime -ge $startedAt) {
            $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
            break
        }
        Start-Sleep -Milliseconds 500
    }
    if ($null -eq $result) {
        if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Tail 85 }
        throw "Diagnostic did not produce a result. See $logPath"
    }
    $result | ConvertTo-Json -Depth 8
    if (-not $result.passed) { throw "Diagnostic failed. See $logPath" }
    Start-Sleep -Seconds 2
} finally {
    $ownedProcess.Refresh()
    if (-not $ownedProcess.HasExited) { Stop-Process -Id $ownedProcess.Id }
    foreach ($replacement in Get-CimInstance Win32_Process -Filter "Name='KingdomInc.exe'") {
        if ($replacement.ExecutablePath -eq $gameExe -and $replacement.CommandLine.Contains($logPath)) { Stop-Process -Id $replacement.ProcessId }
    }
}
