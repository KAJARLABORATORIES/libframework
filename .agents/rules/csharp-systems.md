---
trigger: always_on
---

# C# Systems & Low-Allocation Invariants

Permanent code-level invariants for `kajarlabs.Framework` (`net11.0`, `LangVersion=latest`, `Nullable=enable`).

## Hot-Path Allocation Bans
Applies to per-frame and per-element paths (`Game.Update`/`Draw`, `Drawable` invalidation/traversal/draw-node generation, `GpuQuadBatcher`, `SdlGpuRenderer`, `InputManager`, `Scheduler.Update`):
- **No LINQ:** Never use `System.Linq` queries or extension methods on hot paths.
- **No boxing:** Avoid passing value types to `object` or non-generic APIs, boxing structs via interface calls (constrain generics with `where T : struct, I...` when needed), or unguarded `Enum` boxing.
- **No per-frame closures or delegate allocations:** Never capture locals/instance state in lambdas inside loops. Use `static` local functions (`csharp_prefer_static_local_function`), `static` lambdas, or cached delegates.
- **No interface enumeration:** Never `foreach` over `IEnumerable<T>`, `IReadOnlyList<T>`, or `IList<T>` in hot paths. Iterate concrete arrays, `Span<T>`, or `List<T>` by index or struct enumerator.
- **No transient heap buffers or strings:** Avoid `new T[]`, `new List<T>()`, `params T[]` (prefer `params ReadOnlySpan<T>`), string interpolation, or `ToString()` per frame. Use `[LoggerMessage]` source-generated logging on runtime paths.

## Memory, Spans & Data Layout
- Prefer `Span<T>` / `ReadOnlySpan<T>`, bounded `stackalloc` (guarded with a small upper bound and heap fallback), pre-allocated reusable buffers (`GpuQuadBatcher`, `InputManager`), or `ArrayPool<T>.Shared`.
- Every `ArrayPool<T>.Shared.Rent(...)` must track the requested length and guarantee `Return(...)` on success, exception, and disposal (see `GpuTextureManager`).
- GPU, math, and input snapshot structs (`TexturedVertex2D`, `Color4`, `Quad`, `InputState`, `KeyboardState`, `MouseState`) must be `readonly struct` (when immutable), marked `[StructLayout(LayoutKind.Sequential)]`, implement `IEquatable<T>`, and remain unmanaged-compatible (`!RuntimeHelpers.IsReferenceOrContainsReferences<T>()`).
- Pass large `readonly struct` values by `in` on internal hot paths to avoid struct copies.

## Types & Dispatch
- Mark concrete classes `sealed` by default to enable JIT/AOT devirtualization. Omit `sealed` only on intentional extension bases (e.g., `Drawable`, `CompositeDrawable`, `Container`, `DrawNode`, `Game`, `GameHost`, `Storage`, `Bindable<T>`).
- Keep `internal` implementation details `internal`; never widen visibility to `public` solely for testing.

## Native Interop & SDL3 Boundary
- All `SDL3-CS` calls must stay strictly inside `internal` adapters (`SdlPlatformBackend`, `SdlGpuBackend`, `SdlLogRouter`, `UserDataLocation`). Zero `SDL3` types may appear in `public` or `protected` APIs.
- For any new direct P/Invoke in `libs/`, use source-generated `[LibraryImport]` (never `[DllImport]`), blittable types, and explicit marshaling.
- Any delegate passed as a native callback must remain strongly rooted for the lifetime of its registration (see `SdlLogRouter.callback`).

## NativeAOT & Reflection
- Zero runtime reflection (`System.Reflection`), `dynamic`, `Expression.Compile`, or `System.Reflection.Emit` on runtime paths in `libs/framework`.
- Use compile-time code generation instead: `kajarlabs.Framework.CodeAnalysis` (`[Cached]`/`[Resolved]` on `partial` `Drawable`/`Game` types), `[LoggerMessage]`, and `[LibraryImport]`.
- Do not suppress compiler, Roslyn, trim (`IL2xxx`), or AOT (`IL3xxx`) warnings without a written justification.
