param(
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

try {
    Set-Location $repoRoot
    if (-not (Test-Path '.\UnMessage.Server\UnMessage.Server.csproj')) {
        throw 'Project file UnMessage.Server.csproj was not found.'
    }

    Write-Host '[UnMessage] Starting server...'
    dotnet run --project .\UnMessage.Server\UnMessage.Server.csproj
}
catch {
    Write-Host ('[UnMessage] Run server failed: {0}' -f $_.Exception.Message) -ForegroundColor Red
}
finally {
    if (-not $NoPause) {
        Read-Host 'Press Enter to close'
    }
}
