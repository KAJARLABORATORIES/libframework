<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/logo-dark.png">
    <source media="(prefers-color-scheme: light)" srcset="assets/logo-light.png">
    <img alt="kajar.framework" src="assets/logo-light.png" width="290">
  </picture>
</p>

# kajar.framework

[![Build status](https://github.com/KAJARLABORATORIES/libframework/actions/workflows/ci.yml/badge.svg?branch=develop&event=push)](https://github.com/KAJARLABORATORIES/libframework/actions/workflows/ci.yml)
[![GitHub release](https://img.shields.io/github/v/release/KAJARLABORATORIES/libframework)](https://github.com/KAJARLABORATORIES/libframework/releases/latest)

A lean, extensible 2D game framework for .NET.

## Objectives

`kajarlabs.Framework` is built around predictable low-allocation performance, first-class NativeAOT compatibility, and clean cross-platform abstractions on top of .NET and SDL3.

- **NativeAOT & trimming first** — libraries are `IsAotCompatible` with compile-time source generation (`[Cached]`/`[Resolved]` DI, `[LoggerMessage]`) instead of runtime reflection.
- **Cross-platform SDL3 & GPU rendering** — windowing and hardware-accelerated rendering across Vulkan, Direct3D 12, and Metal behind a managed .NET API.
- **Composable scene & testing** — hierarchical 2D scene graph (`Drawable`, `Container`, `Box`, `Sprite`), reactive bindables, unified input/key bindings, and deterministic headless testing via `HeadlessGameHost`.

## Requirements

- A desktop platform (Windows, Linux, or macOS) with the [.NET 11 SDK](https://dotnet.microsoft.com/download) (see [`global.json`](../global.json)).
- NativeAOT build prerequisites for your platform when publishing [`samples/aot-smoke`](../samples/aot-smoke).
- An IDE with C# and EditorConfig support, such as [JetBrains Rider](https://www.jetbrains.com/rider/), [Visual Studio](https://visualstudio.microsoft.com/vs/), or [Visual Studio Code](https://code.visualstudio.com/).

### Building

Open `kajar-framework.slnx` in your IDE or build from the command line:

```bash
dotnet build
dotnet test
```

You can also run any sample directly from [`samples/`](../samples/), for example:

```bash
dotnet run --project samples/interactive-input
```

### Verification

The full verification suite (formatting, Release build, xUnit v3 tests, NativeAOT smoke publish/run, and NuGet packing) can be run with `./scripts/check.ps1` or `./scripts/check.sh`.

## Contributing

Contributions can be made via pull requests targeting `develop`.

If you're unsure of what you can help with, check out the [list of open issues](https://github.com/KAJARLABORATORIES/libframework/issues). Please open an issue before starting work on new features or architectural changes, and make sure `./scripts/check.ps1` (or `./scripts/check.sh`) passes before submitting.

## License

This framework is licensed under the [MIT license](https://opensource.org/licenses/MIT). Please see [the license file](../COPYING) for more information.
