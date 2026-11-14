#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
shader_dir="$repo_root/libs/framework/Resources/Shaders"
compiled_dir="$shader_dir/Compiled"

check_only=false
shadercross_bin="${SHADERCROSS:-}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --check)
      check_only=true
      shift
      ;;
    --shadercross)
      shadercross_bin="$2"
      shift 2
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 1
      ;;
  esac
done

if [[ -z "$shadercross_bin" ]]; then
  if command -v shadercross >/dev/null 2>&1; then
    shadercross_bin="$(command -v shadercross)"
  fi
fi

if [[ -z "$shadercross_bin" || ! -x "$shadercross_bin" ]]; then
  echo "shadercross CLI was not found. Pass --shadercross <path> or set SHADERCROSS." >&2
  exit 1
fi

if [[ "$check_only" == "true" ]]; then
  output_dir="$(mktemp -d)"
  trap 'rm -rf "$output_dir"' EXIT
else
  output_dir="$compiled_dir"
  mkdir -p "$output_dir"
fi

compile_one() {
  local name="$1"
  local stage="$2"
  local src="$shader_dir/$name.hlsl"

  if [[ ! -f "$src" ]]; then
    echo "Missing shader source: $src" >&2
    exit 1
  fi

  for pair in "SPIRV:spv" "DXIL:dxil" "MSL:msl"; do
    local dest="${pair%%:*}"
    local ext="${pair##*:}"
    echo "==> Compiling $name.hlsl -> $name.$ext"
    "$shadercross_bin" "$src" -s HLSL -d "$dest" -t "$stage" -e main -o "$output_dir/$name.$ext"
  done
}

compile_one "Sprite2D.vert" "vertex"
compile_one "Sprite2D.frag" "fragment"

if [[ "$check_only" == "true" ]]; then
  for file in Sprite2D.vert.spv Sprite2D.vert.dxil Sprite2D.vert.msl Sprite2D.frag.spv Sprite2D.frag.dxil Sprite2D.frag.msl; do
    if [[ ! -f "$compiled_dir/$file" ]]; then
      echo "Missing committed shader artifact: $file" >&2
      exit 1
    fi
    if ! cmp -s "$output_dir/$file" "$compiled_dir/$file"; then
      echo "Shader artifact out of date: $file" >&2
      exit 1
    fi
  done
  echo "All compiled shader artifacts match the HLSL sources."
else
  echo "Shader compilation completed."
fi
