# monogame/: MonoGame v3.8.6 fork (Vulkan), copied from WingSim

Etervale renders with MonoGame's Native platform on Vulkan through this fork: the native runtime (`runtime/`), the
framework built from source (`framework/`) and the SM 6 effect processor (`pipeline/`).

## Etervale's copy

- **Provenance:** copied on 2026-10-18 from WingSim (`F:\WingSim\monogame`, the files tracked by git) at WingSim commit
  `d43702931e764cb34b180677c68860ff3069b5dd`. WingSim's own description of the fork follows below unchanged; its paths
  (`src/WingSim.Game`, `.github/workflows/mgruntime.yml`, `docs/research/12-monogame-backend.md`, `tools/parity`) are
  WingSim's.
- **Two copies, no sharing:** Etervale and WingSim each keep their copy. A fix made in one must be ported to the other by
  hand: the files, the change list of `runtime/README.md`, the document in `issues/` and the interface version (both
  copies' framework and runtime must agree on it, or each refuses the other's library).
- **Etervale's changes so far:**
  - `runtime/build-windows.ps1`: the Windows build of the CI job run locally (README.md of the repository, Build).
  - Issue 013, NoOverwrite uploads in place (runtime change 25, interface version 4): SpriteBatch and
    `DrawUser*Primitives` cost 0.8 ms per texture change before it.
  - The framework's runtime-version message names Etervale's build commands.
- **How Etervale uses it:** the client libraries reference `framework/MonoGame.Framework/MonoGame.Framework.Native.csproj`;
  the client references the processor (`pipeline/MonoGame.Effect.Processor`) without its assembly, so it is built
  before `Client/PristonTale/Content/Content.mgcb` loads it; `Client/MonoGameRuntime.targets` requires
  `runtime/bin/<rid>/` and copies the library next to the client and the regression runner. Built files
  (`runtime/bin`, `runtime/build`, the fetched SDL2 and FAudio, `pipeline/bin`) are git-ignored and never committed.
- **Licences:** MonoGame's code is under the Microsoft Public License (`LICENSE-MonoGame.txt`); the vendored and fetched
  third-party code keeps its own licences (`runtime/CREDITS.md`, `framework/CREDITS.md`, `pipeline/CREDITS.md`).

## WingSim's description


WingSim renders with MonoGame's Native platform on Vulkan (`docs/research/12-monogame-backend.md`). Both halves of
MonoGame that WingSim runs are forked here, as **source versioned in WingSim's repository** (owner's rule: no git
submodule, no NuGet package for the framework, third-party code pinned by commit). Nothing machine-built is committed:
the native library is built by `runtime/build.sh` (Linux, macOS) or by CI (Windows, releases), and the C# projects by
`dotnet build`.

| Folder | What | Built by |
|---|---|---|
| `runtime/` | The native runtime (C++: Vulkan, SDL2, FAudio): MonoGame's `native/monogame` with WingSim's changes, the record of them as `monogame-v3.8.6.patch`, `externals.txt` (pinned dependencies), `build.sh`, `repro/` (verification programs). | `runtime/build.sh` → `runtime/bin/<rid>/` (git-ignored); `.github/workflows/mgruntime.yml` |
| `framework/` | `MonoGame.Framework` (C#), Native platform only, from source; a project reference of `src/WingSim.Engine` and `src/WingSim.Game`. | `dotnet build` (in `WingSim.sln`) |
| `pipeline/` | The effect processor MGCB runs for the game's shaders (`SpirvEffectProcessor`) and the Vulkan effect compiler under it, from MonoGame's source: compute passes, storage resources, SM 6.5 profiles, MGFX 12. | `dotnet build` (in `WingSim.sln`; the Game builds it before its content) → `pipeline/bin/` (git-ignored) |
| `issues/` | One document per change of the fork: the symptom or need, the cause, the change, the test. | — |
| `upstream/` | Notes for the owner, one per runtime fix, as material for an issue he may file himself (MonoGame accepts no AI-written contributions, so nothing is sent upstream as a pull request). | — |
| `LICENSE-MonoGame.txt` | MonoGame's licence, the Microsoft Public License, for everything under `monogame/` except the third-party code credited in each part's `CREDITS.md`. | — |

## Version and provenance

- Upstream: https://github.com/MonoGame/MonoGame, tag **v3.8.6** (commit `c610a529667336f63da323c6057274258727664b`),
  for both halves. How each was taken from the tag: `runtime/README.md` and `framework/README.md`.
- Third-party code is pinned by commit: `runtime/externals.txt` (volk, VMA, Vulkan headers, stb vendored; SDL2 and FAudio
  fetched by `build.sh`), `framework/CREDITS.md` (StbImageSharp, StbImageWriteSharp vendored), `pipeline/CREDITS.md`
  (CppNet vendored).
- Updating MonoGame is a deliberate change: the new tag's files in every part, WingSim's changes reapplied (the patch,
  the `WingSim fork` markers in the C#, `issues/`), every platform rebuilt, the effects compared
  (`tools/parity/mgfx_compare.py`) and the parity captures (`tools/parity`) compared before and after.

## Rules

- **Generic GPU facilities are the fork's public API** (frame capture, timestamps, compute, storage resources); what is
  WingSim's own (the glitch check, the per-section profiler, the ray-tracing effects) stays in `src/`.
- **The framework and the runtime are one version**: the framework checks the runtime's version when it creates the
  graphics system and stops with instructions on a mismatch (`issues/002-runtime-version.md`).
- Every warning is an error here too: the C# builds with MonoGame's compiler settings and `TreatWarningsAsErrors`
  (`framework/Directory.Build.props`, `pipeline/Directory.Build.props`); the C++ with the warning settings of
  `runtime/src/premake5.lua`.
- The fork's GPU API beyond XNA: `GraphicsDevice.BeginFrameCapture`/`TryPopFrame` (issue 003), `GpuTimestampPool`
  (004), `GraphicsDevice.Dispatch`/`DispatchIndirect`, `EffectPass.IsCompute`, `Texture2D` with
  `TextureShaderAccess.ReadWrite` (005), `StructuredBuffer` (006), MGFX 12 and the `cs_6_0`/`*_6_5` profiles (007, 008).
