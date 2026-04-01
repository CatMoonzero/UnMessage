param(
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

try {
    Set-Location $repoRoot
    if (-not (Test-Path '.\UnMessage\UnMessage.csproj')) {
        throw 'Project file UnMessage.csproj was not found.'
    }

    Write-Host '[UnMessage] Starting Windows client...'
    dotnet run --project .\UnMessage\UnMessage.csproj
}
catch {
    Write-Host ('[UnMessage] Run client failed: {0}' -f $_.Exception.Message) -ForegroundColor Red
}
finally {
    if (-not $NoPause) {
        Read-Host 'Press Enter to close'
    }
}
