#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

skip_aot=false
rid=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --skip-aot)
      skip_aot=true
      shift
      ;;
    --rid)
      rid="$2"
      shift 2
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 1
      ;;
  esac
done

if [[ -z "$rid" ]]; then
  os="$(uname -s)"
  arch="$(uname -m)"
  case "$arch" in
    x86_64|amd64) arch="x64" ;;
    aarch64|arm64) arch="arm64" ;;
  esac
  case "$os" in
    Linux) rid="linux-$arch" ;;
    Darwin) rid="osx-$arch" ;;
    MINGW*|MSYS*|CYGWIN*) rid="win-$arch" ;;
    *)
      echo "Unsupported OS '$os'; pass --rid explicitly." >&2
      exit 1
      ;;
  esac
fi

echo "==> dotnet restore"
dotnet restore

echo "==> dotnet format --verify-no-changes --no-restore"
dotnet format --verify-no-changes --no-restore

echo "==> dotnet build -c Release --no-restore"
dotnet build -c Release --no-restore

echo "==> dotnet test -c Release --no-build"
dotnet test -c Release --no-build

if [[ "$skip_aot" == "false" ]]; then
  echo "==> NativeAOT smoke publish & run ($rid)"
  dotnet publish samples/aot-smoke -c Release -r "$rid" -o artifacts/aot
  exe=""
  if [[ "$rid" == win-* ]]; then
    exe=".exe"
  fi
  "./artifacts/aot/kajarlabs.Framework.Samples.AotSmoke$exe"
fi

echo "==> dotnet pack -c Release --no-build"
dotnet pack -c Release --no-build -o artifacts/packages

echo "All local checks passed."
