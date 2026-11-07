param(
    [ValidateRange(1, 65535)]
    [int]$Port = 7777,
    [string]$ProjectPath = (Join-Path $PSScriptRoot '../..')
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath $ProjectPath).Path
$serverExe = Join-Path $projectRoot 'Builds/Network/WindowsServer/MadMachinesServer.exe'
$clientExe = Join-Path $projectRoot 'Builds/Network/Client/MadMachinesClient.exe'
foreach ($executable in @($serverExe, $clientExe)) {
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Сначала соберите Windows client и Windows dedicated server: отсутствует $executable"
    }
}

# Each run has its own stop file and logs. Existing runs and their output are never overwritten.
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$logDirectory = Join-Path $projectRoot "Logs/Network/$runId"
New-Item -ItemType Directory -Path $logDirectory | Out-Null
$stopFile = Join-Path $logDirectory 'server.stop'
$serverLog = Join-Path $logDirectory 'server.log'
$serverProcess = $null
$clients = @()

try {
    $serverProcess = Start-Process -FilePath $serverExe -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru -ArgumentList @(
        '-batchmode', '-nographics', '-mm-bind', '127.0.0.1', '-mm-port', "$Port",
        '-mm-stop-file', ('"' + $stopFile + '"'), '-logFile', ('"' + $serverLog + '"')
    )
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $serverReady = $false
    do {
        if ($serverProcess.HasExited) { throw "Сервер завершился с кодом $($serverProcess.ExitCode). Журнал: $serverLog" }
        if (Test-Path -LiteralPath $serverLog) {
            $serverReady = [bool](Select-String -LiteralPath $serverLog -SimpleMatch '[MM NET] Сервер готов.' -Quiet)
        }
        if (-not $serverReady) { Start-Sleep -Milliseconds 200 }
    } while (-not $serverReady -and [DateTime]::UtcNow -lt $deadline)
    if (-not $serverReady) { throw "Нет подтверждения готовности за 30 секунд. Журнал: $serverLog" }

    for ($index = 1; $index -le 2; $index++) {
        $clientLog = Join-Path $logDirectory "client-$index.log"
        $clients += Start-Process -FilePath $clientExe -WorkingDirectory $projectRoot -PassThru -ArgumentList @(
            '-mm-address', '127.0.0.1', '-mm-port', "$Port", '-screen-fullscreen', '0',
            '-screen-width', '960', '-screen-height', '600', '-logFile', ('"' + $clientLog + '"')
        )
    }
}
catch {
    if ($null -ne $serverProcess -and -not $serverProcess.HasExited) {
        New-Item -ItemType File -Path $stopFile -Force | Out-Null
    }
    foreach ($clientProcess in $clients) {
        if (-not $clientProcess.HasExited) { $clientProcess.CloseMainWindow() | Out-Null }
    }
    throw
}

Write-Host "Запущены сервер и два клиента. Журналы: $logDirectory"
Write-Host "Для штатной остановки: New-Item -ItemType File -Path '$stopFile'"
[PSCustomObject]@{
    Server = $serverProcess
    Clients = $clients
    StopFile = $stopFile
    Logs = $logDirectory
}
