param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug',
    [switch]$Install,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

try {
    Set-Location $repoRoot
    if (-not (Test-Path '.\UnMessage.Android\UnMessage.Android.csproj')) {
        throw 'Project file UnMessage.Android.csproj was not found.'
    }

    Write-Host ('[UnMessage] Building Android client ({0})...' -f $Configuration)
    dotnet build .\UnMessage.Android\UnMessage.Android.csproj -c $Configuration

    if ($Install) {
        $apk = Get-ChildItem .\UnMessage.Android\bin\$Configuration\net10.0-android -Filter *.apk -Recurse | Select-Object -First 1
        if (-not $apk) {
            throw 'APK was not found. Check Android workload and packaging output.'
        }

        Write-Host '[UnMessage] Removing old package (if exists)...'
        adb uninstall com.unmessage.android | Out-Null

        Write-Host ('[UnMessage] Installing APK: {0}' -f $apk.FullName)
        adb install -r "$($apk.FullName)"
    }

    Write-Host '[UnMessage] Android build completed.'
}
catch {
    Write-Host ('[UnMessage] Android script failed: {0}' -f $_.Exception.Message) -ForegroundColor Red
}
finally {
    if (-not $NoPause) {
        Read-Host 'Press Enter to close'
    }
}
