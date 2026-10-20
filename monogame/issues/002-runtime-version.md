# 002. The framework checks the runtime's interface version

Part: runtime and framework. Phase R2.0.

## Need

With the framework built from source, the C# side and the native library change together: the framework calls the
fork's exports (frame capture, timestamps, and from R2.0 compute and storage resources), and an export's arguments can
change. A library from elsewhere fails late and obscurely:
- the official `MonoGame.Runtime` library: at the first missing export (`EntryPointNotFoundException` in the middle of a
  frame);
- a stale build of the fork (a worktree that falls back to the main checkout's library, a Windows CI artifact of another
  commit): an export whose arguments changed reads garbage.

## Cause

`DllImport` binds by name at the first call and checks nothing else.

## Change

- Runtime: `MGG_WingSim_RuntimeVersion()` returns `MGVK_WINGSIM_RUNTIME_VERSION` (`vulkan/MGG_Vulkan.cpp`).
- Framework: `MGG.WingSimRuntimeVersion` is the version it is written for (`Platform/Native/Graphics.Interop.WingSim.cs`);
  `MGG.EnsureRuntimeVersion()` runs first in `NativeGamePlatform`'s constructor and throws an
  `InvalidOperationException` that names the found and the needed version and how to rebuild (a library without the
  export reports version 0).
- Both numbers change together with every change of an exported function. Version 1: the exports of the runtime at the
  start of R2.0 (changes 1–19). Version 2: compute and storage resources (issues 005, 006). Version 3: pipeline statistics (issue 012).

## Test

- The game starts with the library built from the same commit (every capture of R2.0).
- With the main checkout's older library copied in its place, the game stops at once with the message instead of
  failing in a frame (checked by hand, 2026-10-15).
