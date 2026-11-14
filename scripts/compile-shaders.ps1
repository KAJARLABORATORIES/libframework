param(
    [string]$ShaderCrossPath = "",
    [switch]$Check
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$shaderDir = Join-Path $repoRoot "libs/framework/Resources/Shaders"
$compiledDir = Join-Path $shaderDir "Compiled"

if ([string]::IsNullOrWhiteSpace($ShaderCrossPath)) {
    if (-not [string]::IsNullOrWhiteSpace($env:SHADERCROSS)) {
        $ShaderCrossPath = $env:SHADERCROSS
    } else {
        $cmd = Get-Command shadercross -ErrorAction SilentlyContinue
        if ($cmd) {
            $ShaderCrossPath = $cmd.Source
        }
    }
}

if ([string]::IsNullOrWhiteSpace($ShaderCrossPath) -or -not (Test-Path $ShaderCrossPath)) {
    throw "shadercross CLI was not found. Pass -ShaderCrossPath <path> or set SHADERCROSS."
}

$outputDir = if ($Check) {
    Join-Path ([System.IO.Path]::GetTempPath()) ("kajarlabs-shaders-check-" + [Guid]::NewGuid().ToString("N"))
} else {
    $compiledDir
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$shaders = @(
    @{ Name = "Sprite2D.vert"; Stage = "vertex"; Source = "Sprite2D.vert.hlsl" },
    @{ Name = "Sprite2D.frag"; Stage = "fragment"; Source = "Sprite2D.frag.hlsl" }
)

$targets = @(
    @{ Dest = "SPIRV"; Extension = "spv" },
    @{ Dest = "DXIL"; Extension = "dxil" },
    @{ Dest = "MSL"; Extension = "msl" }
)

try {
    foreach ($shader in $shaders) {
        $sourcePath = Join-Path $shaderDir $shader.Source
        if (-not (Test-Path $sourcePath)) {
            throw "Missing shader source: $sourcePath"
        }

        foreach ($target in $targets) {
            $outFile = Join-Path $outputDir ("{0}.{1}" -f $shader.Name, $target.Extension)
            Write-Host "==> Compiling $($shader.Source) -> $($shader.Name).$($target.Extension)" -ForegroundColor Cyan
            & $ShaderCrossPath $sourcePath -s HLSL -d $target.Dest -t $shader.Stage -e main -o $outFile
            if ($LASTEXITCODE -ne 0) {
                throw "shadercross failed with exit code $LASTEXITCODE for $($shader.Source) ($($target.Dest))."
            }
        }
    }

    if ($Check) {
        $mismatches = @()

        foreach ($shader in $shaders) {
            foreach ($target in $targets) {
                $fileName = "{0}.{1}" -f $shader.Name, $target.Extension
                $generatedPath = Join-Path $outputDir $fileName
                $committedPath = Join-Path $compiledDir $fileName

                if (-not (Test-Path $committedPath)) {
                    $mismatches += "$fileName (missing committed artifact)"
                    continue
                }

                $generatedHash = (Get-FileHash -Path $generatedPath -Algorithm SHA256).Hash
                $committedHash = (Get-FileHash -Path $committedPath -Algorithm SHA256).Hash

                if ($generatedHash -ne $committedHash) {
                    $mismatches += "$fileName (hash mismatch: committed=$committedHash generated=$generatedHash)"
                }
            }
        }

        if ($mismatches.Count -gt 0) {
            throw ("Compiled shader artifacts are out of date:`n - " + ($mismatches -join "`n - "))
        }

        Write-Host "All compiled shader artifacts match the HLSL sources." -ForegroundColor Green
    } else {
        Write-Host "Shader compilation completed." -ForegroundColor Green
    }
}
finally {
    if ($Check -and (Test-Path $outputDir)) {
        Remove-Item -Path $outputDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
