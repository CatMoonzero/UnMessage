param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

try {
    Set-Location $repoRoot
    if (-not (Test-Path '.\UnMessage.slnx')) {
        throw 'Solution file UnMessage.slnx was not found.'
    }

    Write-Host ('[UnMessage] Building solution ({0})...' -f $Configuration)
    dotnet build .\UnMessage.slnx -c $Configuration
    Write-Host '[UnMessage] Build completed.'
}
catch {
    Write-Host ('[UnMessage] Build failed: {0}' -f $_.Exception.Message) -ForegroundColor Red
}
finally {
    if (-not $NoPause) {
        Read-Host 'Press Enter to close'
    }
}
