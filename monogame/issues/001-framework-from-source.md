# 001. MonoGame.Framework from source

Part: framework (`monogame/framework`), the runtime moved (`native/mgruntime` → `monogame/runtime`). Phase R2.0
(`docs/research/14-specs/R2-ray-tracing.md` §3).

## Need

- Only the native half of MonoGame was WingSim's (`native/mgruntime`). `MonoGame.Framework` (C#) came from the NuGet
  package `MonoGame.Framework.Native` 3.8.6.
- What WingSim had added to the runtime reached C# from outside: `AsyncFrameCapture` and `GpuTimestamps` read the
  internal `GraphicsDevice.Handle` by reflection and called `MGG_FrameCapture_*` and `MGG_TimestampPool_*` through
  `NativeLibrary`.
- That held for two functions. Compute shaders, storage images and structured buffers (ray tracing, R2) need new types
  and techniques in the effect system and new profiles in the content pipeline: a fork of the C# half.

## Cause

The package is a compiled assembly: its internals can only be reached by reflection, and its effect reader and
`EffectPass` cannot be extended.

## Change

- `native/mgruntime` moved to `monogame/runtime` (build script, patch flow, CI and repro unchanged apart from the paths);
  the owner's notes to `monogame/upstream`, MonoGame's licence to `monogame/LICENSE-MonoGame.txt`.
- `monogame/framework/MonoGame.Framework`: the 479 files that v3.8.6's `MonoGame.Framework.Native.csproj` compiles, taken
  from the tag (`framework/README.md` "How it was obtained"), with WingSim's project file. StbImageSharp and
  StbImageWriteSharp at the submodule commits of the tag in `framework/ThirdParty`.
- `src/WingSim.Engine`, `src/WingSim.Game` and `tests/WingSim.Tests` reference the project instead of the package. The
  game no longer takes the `MonoGame.Runtime.*.Vulkan` packages: the fork's library is copied next to the executable
  (`WingSimForkedRuntime`, and `WingSimForkedRuntimePublish` for the release packages), where the framework's
  `DllImport("mgruntime")` finds it.
- The framework does not import the repository's C# settings (WingSim's analyzers and nullable context: upstream code was
  not written for them) but keeps every warning an error; the 27 upstream warnings (and NETSDK1210 below) were fixed at their cause
  (`framework/README.md`). `IsAotCompatible` was dropped: a netstandard2.1 library cannot carry it (NETSDK1210).
- `ArchitectureTests`: the framework project is allowed for the Engine and the Game only, and a project below the Engine
  referencing anything under `monogame/` fails.
- `monogame/runtime/repro`: the repro programs build with the repository's C# settings again (`repro/Directory.Build.props`;
  the runtime folder's empty props, added for the Windows C++ build, had left them without a target framework), on the
  fork's framework, or on the official packages with `WingSimFork=false`.

## Test

- Built unchanged, the assembly is byte for byte the size of the package's (762 880 bytes): the same compile set.
- `dotnet build WingSim.sln` Debug and Release: 0 warnings; the full test suite passes.
- The parity captures (`tools/parity/capture.sh`, all scenes) before and after the R2.0 series are equal within the
  run-to-run noise (numbers in `docs/research/12-monogame-backend.md`, "R2.0").
