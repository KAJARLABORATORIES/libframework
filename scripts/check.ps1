param(
    [string]$Rid = "",
    [switch]$SkipAot
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    $env:DOTNET_NOLOGO = "1"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

    if ([string]::IsNullOrWhiteSpace($Rid)) {
        $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
        if ($IsLinux) {
            $Rid = "linux-$arch"
        } elseif ($IsMacOS) {
            $Rid = "osx-$arch"
        } else {
            $Rid = "win-$arch"
        }
    }

    Write-Host "==> dotnet restore" -ForegroundColor Cyan
    dotnet restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "==> dotnet format --verify-no-changes --no-restore" -ForegroundColor Cyan
    dotnet format --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "==> dotnet build -c Release --no-restore" -ForegroundColor Cyan
    dotnet build -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "==> dotnet test -c Release --no-build" -ForegroundColor Cyan
    dotnet test -c Release --no-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    if (-not $SkipAot) {
        Write-Host "==> NativeAOT smoke publish & run ($Rid)" -ForegroundColor Cyan
        dotnet publish samples/aot-smoke -c Release -r $Rid -o artifacts/aot
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        $exe = if ($Rid.StartsWith("win-")) { ".exe" } else { "" }
        & "artifacts/aot/kajarlabs.Framework.Samples.AotSmoke$exe"
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    Write-Host "==> dotnet pack -c Release --no-build" -ForegroundColor Cyan
    dotnet pack -c Release --no-build -o artifacts/packages
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "All local checks passed." -ForegroundColor Green
}
finally {
    Pop-Location
}
