---
name: native-aot-safety
description: NativeAOT and IL trimming compatibility guide for kajarlabs.Framework. Use when adding or modifying code in libs/framework, resolving IL2xxx/IL3xxx warnings, replacing reflection with source generators, annotating DynamicallyAccessedMembers, adding serialization or P/Invoke interop, or updating samples/aot-smoke.
---

# NativeAOT & Trimming Safety

All packable libraries under `libs/` inherit `<IsAotCompatible>true</IsAotCompatible>` from `libs/Directory.Build.props`, and `Directory.Build.props` enforces `TreatWarningsAsErrors=true` and `CodeAnalysisTreatWarningsAsErrors=true`. End-to-end NativeAOT compilation and execution is verified by `samples/aot-smoke` (`<PublishAot>true</PublishAot>`, `<TrimmerSingleWarn>false</TrimmerSingleWarn>`).

## 1. Eliminating Reflection with Compile-Time Generation
Never introduce `System.Reflection` member discovery, `dynamic`, `Expression.Compile()`, `Reflection.Emit`, `Type.MakeGenericType`, `MethodInfo.MakeGenericMethod`, or `Activator.CreateInstance` on runtime paths in `kajarlabs.Framework`.

Use these repository patterns instead:
- **Dependency Injection (`kajarlabs.Framework.CodeAnalysis`):**
  - `DependencyInjectionGenerator` (`IIncrementalGenerator`) generates `CacheDependencies` and `ResolveDependencies` overrides at compile time for `[Cached]` and `[Resolved]` members on `partial` subclasses of `Drawable` or `Game`.
  - Respect generator diagnostics `KJDI001` (missing `partial` on type/enclosing types), `KJDI002` (invalid `[Resolved]` target), `KJDI003` (invalid `[Cached]` target), `KJDI004` (invalid `[Cached(typeof(...))]` key), and `KJDI005` (containing type must derive from `Drawable` or `Game`).
  - Use `DependencyCollection.Cache<T>()` / `TryGet<T>()` / `Get<T>()` rather than reflection-based registries.
- **Structured Logging:**
  - Use `[LoggerMessage]` source-generated methods (`partial` classes in `GameHost`, `GpuDeviceContext`, `SdlLogRouter`) to eliminate runtime format parsing and boxing.
- **Native Interop:**
  - Keep SDL3 calls inside the internal adapters backed by `SDL3-CS`.
  - For any new direct P/Invoke, use `[LibraryImport]` (compile-time marshaling stubs, zero runtime stub generation) instead of `[DllImport]`. Restrict signatures to blittable types, `Span<T>`/`ReadOnlySpan<T>`, or explicit `StringMarshalling = StringMarshalling.Utf8`.
  - Keep delegates marshaled to native code strongly referenced for the entire registration lifetime (`SdlLogRouter.callback`), or use `[UnmanagedCallersOnly]` function pointers for static native callbacks.
- **Serialization (if introduced):**
  - Use `System.Text.Json` source generation (`[JsonSerializable(typeof(...))]` on a `partial class ... : JsonSerializerContext`) and pass `JsonTypeInfo<T>` explicitly. Never call reflection-based `JsonSerializer.Serialize<T>(value)` without a source-generated context.

## 2. Handling `IL2xxx` (Trim) and `IL3xxx` (AOT) Warnings
Common diagnostics and their proper fixes:
- `IL2026` (*RequiresUnreferencedCode*) / `IL3050` (*RequiresDynamicCode*): Replace the dynamic API with a compile-time generic, source generator, or explicit dispatch table.
- `IL2057` (*Unrecognized value passed to `Type.GetType`*): Eliminate string-based type loading; use explicit factory delegates or switch tables.
- `IL2067`–`IL2091` (*`DynamicallyAccessedMembers` mismatch*): Propagate annotations consistently along the call chain rather than suppressing.
- `IL3051` / `IL3053` (*AOT analysis on reflection/generic instantiation*): Avoid instantiating generic types or methods over runtime `Type` objects, especially with value-type arguments (NativeAOT cannot synthesize new value-type generic instantiations at runtime).

### When Annotations Are Unavoidable
1. Annotate parameters, fields, properties, and generic type parameters with the **narrowest** `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.*)]` flag required (never blanket `All` unless truly needed).
2. If an opt-in diagnostic or cold-path API genuinely cannot be made trim/AOT-safe, mark it with `[RequiresUnreferencedCode("...")]` and/or `[RequiresDynamicCode("...")]` so callers receive compile-time warnings — never silently break AOT consumers.
3. Use `[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "...")]` or `("AOT", "IL3050", ...)` **only** when static analysis cannot prove an invariant that you can prove in writing (e.g., guarded by `RuntimeFeature.IsDynamicCodeSupported` with an AOT-safe fallback branch).

## 3. Validating Library AOT Compatibility
Because `libs/framework` is a class library, build-time analyzers catch annotated violations, while `samples/aot-smoke` exercises real ILC compilation and trimming:
1. Build in Release to trigger Roslyn trim/AOT analyzers enabled by `<IsAotCompatible>true</IsAotCompatible>`:
   ```bash
   dotnet build kajar-framework.slnx -c Release
   ```
2. Whenever you add a new public subsystem, generic type, DI pattern, or native interop path to `kajarlabs.Framework`, add a runtime exercise for it in `samples/aot-smoke/Program.cs` so ILC actually compiles and executes the code path instead of trimming it away.
3. Publish and run `samples/aot-smoke` for the host RID (`win-x64`, `linux-x64`, or `osx-arm64`):
   ```bash
   dotnet publish samples/aot-smoke -c Release -r <rid> -o artifacts/aot
   ./artifacts/aot/kajarlabs.Framework.Samples.AotSmoke
   ```
   With `<TrimmerSingleWarn>false</TrimmerSingleWarn>`, every individual trim/AOT warning inside `kajarlabs.Framework` surfaces directly during publish and fails the build.
