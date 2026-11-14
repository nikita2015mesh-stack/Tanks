[CmdletBinding()]
param([string]$BindAddress = '0.0.0.0')

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$serverDirectory = Join-Path $projectRoot 'Builds/Network/Match-WindowsServer'
$activeBuild = Join-Path $serverDirectory 'active.txt'
if (-not (Test-Path -LiteralPath $activeBuild -PathType Leaf)) {
    throw 'Build the Windows dedicated server first using Tools > Mad Machines > Networking in Unity.'
}

$serverId = ([string](Get-Content -LiteralPath $activeBuild -Raw)).Trim()
if ($serverId -notmatch '\A[0-9a-f]{32}\z') {
    throw "Invalid build ID in $activeBuild. Rebuild the Windows dedicated server."
}

$serverExecutable = Join-Path $serverDirectory "versions/$serverId/MadMachinesServer.exe"
if (-not (Test-Path -LiteralPath $serverExecutable -PathType Leaf)) {
    throw "Server executable not found: $serverExecutable"
}

$runDirectory = Join-Path $projectRoot ('Logs/Network/lan.' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$logFile = Join-Path $runDirectory 'server.log'
$stopFile = Join-Path $runDirectory 'stop'
$quotedStopFile = $stopFile.Replace("'", "''")

Write-Host "Starting match server on $BindAddress (UDP 7777 and 7778)."
Write-Host "Log: $logFile"
Write-Host "To stop from another PowerShell window: New-Item -ItemType File -Path '$quotedStopFile'"
& $serverExecutable -batchmode -nographics -mm-bind $BindAddress -mm-port 7777 -logFile $logFile -mm-stop-file $stopFile
exit $LASTEXITCODE
