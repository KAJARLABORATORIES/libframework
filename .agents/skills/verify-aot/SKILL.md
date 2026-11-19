---
name: verify-aot
description: Run release build with Roslyn/AOT/trim analyzers, execute the xUnit v3 test suite, and publish + execute the NativeAOT smoke binary (samples/aot-smoke). Triggered via /verify-aot or when verifying NativeAOT and trimming compatibility.
---

# `/verify-aot` — Build, Test & NativeAOT Verification

Verify full build hygiene, analyzer compliance (`TreatWarningsAsErrors=true`, `CodeAnalysisTreatWarningsAsErrors=true`, `IsAotCompatible=true` on `libs/framework`), xUnit v3 tests, and real NativeAOT compilation/execution via `samples/aot-smoke`.

## Environment Setup
Set these variables before invoking `dotnet` to keep output clean and avoid telemetry failures in restricted environments:
- PowerShell: `$env:DOTNET_NOLOGO = "1"; $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"`
- Bash: `export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1`

Determine the host Runtime Identifier (`<rid>`):
- Windows x64: `win-x64` (executable suffix `.exe`)
- Linux x64: `linux-x64`
- macOS Apple Silicon: `osx-arm64`

## Steps

### 1. Restore Solution
```bash
dotnet restore kajar-framework.slnx
```

### 2. Release Build with Analyzers (Roslyn + AOT/Trim on `libs/framework`)
```bash
dotnet build kajar-framework.slnx -c Release --no-restore
```
This compiles `kajarlabs.Framework.CodeAnalysis`, runs `DependencyInjectionGenerator` across the solution, and runs Roslyn AOT/trim analyzers enabled by `<IsAotCompatible>true</IsAotCompatible>` in `libs/Directory.Build.props`.

### 3. Execute xUnit v3 Test Suite (Microsoft Testing Platform)
```bash
dotnet test -c Release --no-build
```
Runs `tests/framework/kajarlabs.Framework.Tests.csproj` (headless host + generator tests + unit tests). Add `--output Detailed` if diagnosing a failing test.

### 4. Publish & Execute NativeAOT Smoke Sample
`samples/aot-smoke/kajarlabs.Framework.Samples.AotSmoke.csproj` sets `<PublishAot>true</PublishAot>` and `<TrimmerSingleWarn>false</TrimmerSingleWarn>`:
```bash
dotnet publish samples/aot-smoke -c Release -r <rid> -o artifacts/aot
```
Then run the compiled native binary and assert exit code `0`:
- Windows: `./artifacts/aot/kajarlabs.Framework.Samples.AotSmoke.exe`
- Linux/macOS: `./artifacts/aot/kajarlabs.Framework.Samples.AotSmoke`

### 5. Optional Full Pre-PR Verification
To also run `dotnet format --verify-no-changes` and `dotnet pack` (with `EnablePackageValidation=true`):
- Windows: `./scripts/check.ps1`
- Linux/macOS: `./scripts/check.sh`

## Reporting & Triage
Report pass/fail for each step with exact diagnostic codes on failure:
- **`IL2xxx`:** Trimming incompatibility — check reflection, unannotated `Type` flow, or missing `[DynamicallyAccessedMembers]`.
- **`IL3xxx`:** NativeAOT incompatibility — check dynamic code generation, `MakeGenericType`/`MakeGenericMethod`, or unannotated reflection.
- **`KJDI001`–`KJDI005`:** DI source-generator contract violation in `kajarlabs.Framework.CodeAnalysis`.
- **Smoke coverage reminder:** If new public APIs or subsystems were added to `kajarlabs.Framework` without corresponding coverage in `samples/aot-smoke/Program.cs`, note the gap so ILC actually exercises them.
