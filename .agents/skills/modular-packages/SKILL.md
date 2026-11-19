---
name: modular-packages
description: Module boundaries, project dependency graph, native dependency isolation, and public API discipline for kajarlabs.Framework. Use when adding or restructuring .csproj projects, adding ProjectReference/PackageReference entries, designing public vs internal APIs, modifying InternalsVisibleTo, or wrapping native libraries like SDL3-CS.
---

# Modular Package Architecture

## 1. Real Repository Layers (`kajar-framework.slnx`)
All project dependencies must form a strict acyclic DAG flowing downward only:

```text
samples/*  (6 executable apps; includes aot-smoke)
    ├── ProjectReference ──► libs/framework/kajarlabs.Framework.csproj
    └── Analyzer         ──► libs/code-analysis/kajarlabs.Framework.CodeAnalysis.csproj (via samples/Directory.Build.props)

tests/framework/kajarlabs.Framework.Tests.csproj  (xUnit v3 on Microsoft.Testing.Platform)
    ├── ProjectReference ──► libs/framework/kajarlabs.Framework.csproj  (InternalsVisibleTo)
    └── ProjectReference + Analyzer ──► libs/code-analysis/kajarlabs.Framework.CodeAnalysis.csproj

libs/framework/kajarlabs.Framework.csproj  (net11.0, IsAotCompatible=true, IsPackable=true)
    └── Analyzer only (ReferenceOutputAssembly="false", PrivateAssets="all")
                         ──► libs/code-analysis/kajarlabs.Framework.CodeAnalysis.csproj
                             (packed into analyzers/dotnet/cs of kajarlabs.Framework.nupkg)

libs/code-analysis/kajarlabs.Framework.CodeAnalysis.csproj  (netstandard2.0, IsRoslynComponent=true)
    └── (no ProjectReference — Roslyn APIs only: Microsoft.CodeAnalysis.CSharp)
```

### Internal Subsystem Layering in `kajarlabs.Framework`
Keep namespace dependencies unidirectional inside `libs/framework`:
1. **Primitives & Core Utilities:** `Numerics`, `Timing`, `Threading`, `Bindables`, `DependencyInjection` — zero dependency on `Platform`, `Scene`, `Input`, or `SDL3`.
2. **Infrastructure & Contracts:** `IO`, `Logging`, `Graphics` (public value types & `ITexture`) — `IO`/`Logging` isolate SDL3 strictly in internal helpers (`UserDataLocation`, `SdlLogRouter`).
3. **Rendering Engine:** `Graphics.Rendering` (`IRenderer`, `TexturedVertex2D` public; `IHostRenderer`, `IGpuBackend`, `SdlGpuBackend`, `SdlGpuRenderer`, `NullRenderer`, `GpuQuadBatcher`, `GpuTextureManager`, `GpuPipelineCache`, `GpuSamplerCache`, `GpuDeviceContext` internal).
4. **Scene & Input:** `Scene` (`Drawable`, `CompositeDrawable`, `Container`, `Box`, `Sprite`, `DrawNode`) and `Input` (`InputState`, `KeyboardState`, `MouseState`, `InputManager`, `KeyBindingContainer<T>`, `UIEvent`).
5. **Host & Application Entry:** `Platform` (`GameHost`, `HeadlessGameHost`, `GameWindow`, `HostOptions`; internal `IPlatformBackend`, `SdlPlatformBackend`) and root `kajarlabs.Framework.Game`.

## 2. Adding New Modules Under `libs/`
When splitting a feature into a new package under `libs/<module>/`:
- Inherit `libs/Directory.Build.props` automatically (`IsAotCompatible=true`, `IsPackable=true`, `EnablePackageValidation=true`, `MinVer`, symbol packages).
- Register it in `kajar-framework.slnx` and keep all NuGet package versions exclusively in `Directory.Packages.props` (no `Version` attributes in `.csproj`).
- Never introduce a dependency from a lower layer (`libs/*`) to `tests/*` or `samples/*`.
- Never give a runtime library a compile-time runtime reference to a Roslyn generator project (`libs/code-analysis`); always attach analyzers with `OutputItemType="Analyzer" ReferenceOutputAssembly="false" PrivateAssets="all"`.

## 3. Native & Third-Party Dependency Isolation
- `SDL3-CS` (`SDL3-CS`, `SDL3-CS.Windows`, `SDL3-CS.Linux`, `SDL3-CS.MacOS`) is an implementation detail of `kajarlabs.Framework`.
- `using SDL3;` is restricted to the 4 `internal` adapter files:
  - `libs/framework/Graphics/Rendering/SdlGpuBackend.cs`
  - `libs/framework/Platform/SdlPlatformBackend.cs`
  - `libs/framework/Logging/SdlLogRouter.cs`
  - `libs/framework/IO/UserDataLocation.cs`
- Expose framework-owned abstractions (`IRenderer`, `ITexture`, `Viewport`, `BlendingParameters`, `Key`, `MouseButton`, `GameWindow`, `GameHost`) and hide SDL3 handles (`nint`) behind internal seams (`IGpuBackend`, `IPlatformBackend`) so unit tests can substitute `StubGpuBackend` or `HeadlessGameHost` without SDL video hardware.
- Apply the same adapter-seam rule to any future native library (e.g., audio, text shaping, image codecs): zero third-party native types in `public` or `protected` signatures.

## 4. `InternalsVisibleTo` & Public API Surface Discipline
- `kajarlabs.Framework.csproj` grants `InternalsVisibleTo` **only** to `kajarlabs.Framework.Tests`. Do not add `samples/*` or other runtime packages to `InternalsVisibleTo`.
- Default every new type and member to `internal` (or `private`) until there is a clear, stable consumer need.
- Never promote an `internal` type or member to `public` solely to test it.
- Design public base classes for forward compatibility: keep abstract cores minimal and add future extension points as `virtual` with defaults (see `Storage`'s `*Core` pattern).
- Remember `EnablePackageValidation=true` is enabled in `libs/Directory.Build.props`: treat removals or signature changes on `public`/`protected` types as breaking API changes.
