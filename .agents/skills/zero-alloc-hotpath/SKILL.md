---
name: zero-alloc-hotpath
description: Low-allocation C# patterns for kajarlabs.Framework hot paths. Use when implementing or optimizing per-frame game loops, scene graph updates, draw-node generation, GPU quad batching, input dispatch, scheduler updates, struct layouts, pooling, MemoryMarshal/Unsafe operations, or allocation benchmarks.
---

# Zero-Allocation Hot-Path Patterns

Target hot paths in `kajarlabs.Framework`:
- `Game.Update` / `Game.Draw` and `TripleBuffer<DrawNode?>` handoff
- `Drawable` invalidation, transform/color caching, `UpdateSubTree`, `GenerateDrawNodeSubtree`, `CompositeDrawNode.Draw`
- `GpuQuadBatcher.DrawQuad` / `flushQueuedQuads`, `GpuTextureManager`, `SdlGpuRenderer`, `SdlGpuBackend`
- `InputManager` queue building, hover/drag/click dispatch, `KeyCombination` matching
- `Scheduler.Update`

## 1. Struct Layout & Unmanaged Snapshots
- Mark immutable math, vertex, and snapshot structs `public readonly struct` with `[StructLayout(LayoutKind.Sequential)]` and `IEquatable<T>` (e.g., `TexturedVertex2D`, `Color4`, `Quad`, `RectangleF`, `RectangleI`, `Viewport`, `InputState`, `KeyboardState`, `MouseState`).
- Enforce zero managed references for snapshot/GPU structs with `RuntimeHelpers.IsReferenceOrContainsReferences<T>()` in tests and `samples/aot-smoke/Program.cs`.
- Pack fixed-cardinality state into bitmasks rather than collections: see `KeyboardState` (4×`ulong` bitset with `BitOperations.PopCount` and `BitOperations.TrailingZeroCount`).
- Order struct fields from widest to narrowest alignment (`ulong`/`nint`/`double` → `int`/`float`/`Vector2` → `short` → `byte`/`bool`) when layout is not fixed by a shader or native ABI.
- Pass large `readonly struct` arguments by `in` on internal hot paths (e.g., `in GpuOffscreenFrame`, `in Matrix4x4`, `in InputState`). Never pass a non-`readonly` struct by `in` if methods are called on it — C# emits a hidden defensive copy.

## 2. `MemoryMarshal` & `Unsafe`
- Reinterpret pre-allocated byte staging buffers without copying or allocating typed arrays:
  ```csharp
  // GpuQuadBatcher.cs
  private static readonly int vertex_size_bytes = Unsafe.SizeOf<TexturedVertex2D>();
  var vertices = MemoryMarshal.Cast<byte, TexturedVertex2D>(vertexStagingBytes);
  ```
- View a single unmanaged struct as raw bytes for uniform uploads without pinning or allocating `byte[]`:
  ```csharp
  // SdlGpuBackend.cs
  var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in matrix), 1));
  ```
- When iterating a private `List<T>` in tight loops without mutating its length, `CollectionsMarshal.AsSpan(list)` removes bounds-check overhead and avoids enumerator copies. Never add/remove elements while the span is live.
- Use `Unsafe.SizeOf<T>()` instead of `Marshal.SizeOf<T>()` (the latter can incur marshaling metadata overhead and is less AOT-friendly).

## 3. Allocation-Free Buffers & Pooling
- **Pre-allocated instance scratch buffers:** Reuse private `List<T>`, `HashSet<T>`, and `byte[]` fields across frames (`InputManager.positionalQueue`, `InputManager.tempTargets`, `GpuQuadBatcher.vertexStagingBytes`). Call `.Clear()` without trimming capacity.
- **Double/triple-buffer handoff across threads:** Swap pre-allocated buffers via `Interlocked.Exchange` or `TripleBuffer<T>` instead of cloning lists per frame (see `GameHost.pendingInputEvents`/`swapInputEvents`, `Game.drawNodeBuffer`, and `SpriteBatchBenchmarkGame`).
- **Bounded `stackalloc` with heap fallback:**
  ```csharp
  // SdlGpuBackend.cs
  var sdlBindings = bindingsSpan.Length <= 8
      ? stackalloc SDL.GPUVertexBufferDescription[bindingsSpan.Length]
      : new SDL.GPUVertexBufferDescription[bindingsSpan.Length];
  ```
  Never use unbounded `stackalloc` inside a loop (stack frames do not unwind per iteration; lift the buffer above the loop or bound it strictly).
- **`ArrayPool<T>.Shared` for variable-size transient buffers:**
  - Always store and pass the logical `byteCount` separately (`Rent` returns an array of length `>= byteCount`).
  - Guarantee `ArrayPool<T>.Shared.Return(buffer)` in `catch`, `finally`, and `Dispose()` paths (see `GpuTextureManager.EnqueueUpload` and `FlushPendingUploads`).
  - Pass `clearArray: false` (default) for primitive/unmanaged arrays; pass `clearArray: true` only when renting arrays holding managed references so GC can collect them.

## 4. Preventing Closures, Delegates, Boxing & Hidden Allocations
- Prefer `static` local functions and `static` lambdas; pass state explicitly as parameters instead of capturing `this` or locals (enforced by `csharp_prefer_static_local_function` in `.editorconfig`).
- Cache delegates as `readonly` fields when subscribing to events or passing callbacks to `Scheduler` / native APIs (`GameHost.onThreadException`, `SdlLogRouter.callback`).
- Avoid `foreach` on `IEnumerable<T>` / `IReadOnlyList<T>` / `IList<T>` in hot paths — boxes struct enumerators (`List<T>.Enumerator`) onto the heap. Loop with `for (var i = 0; i < items.Count; i++)` over concrete `List<T>`, `T[]`, or `ReadOnlySpan<T>`.
- Avoid `params T[]` on hot APIs; expose `ReadOnlySpan<T>` (or `params ReadOnlySpan<T>`) overloads.
- Replace `ILogger.LogInformation($"...")` with `[LoggerMessage]` `private static partial` methods (zero allocation when level is disabled, zero boxing of value-type args).
- Check `EqualityComparer<T>.Default` for generic structs implementing `IEquatable<T>` (devirtualized in .NET); avoid `object.Equals` on unconstrained value types.

## 5. Measuring Allocations & Throughput
- **Deterministic allocation assertions in xUnit v3 (`kajarlabs.Framework.Tests`):**
  Use `HeadlessGameHost` to warm up JIT/caches for several frames, then bracket steady-state execution with `GC.GetAllocatedBytesForCurrentThread()` on a single thread to assert zero (or strictly bounded) bytes allocated per frame.
- **Batching throughput & frame telemetry:**
  Run `samples/sprite-batch-benchmark` in Release:
  ```bash
  dotnet run -c Release --project samples/sprite-batch-benchmark -- --count 10000 --duration 5
  ```
- **Profiling & microbenchmarks:**
  - For live allocation sampling, use `dotnet-trace` (`--profile gc-verbose` / `gc-collect`) or `dotnet-gcdump` against a Release build.
  - This repository does not currently include a BenchmarkDotNet project in `kajar-framework.slnx`. If adding microbenchmarks, place them in a dedicated non-packable project (never inside `libs/framework`) and enable `[MemoryDiagnoser]` with a Release build.
