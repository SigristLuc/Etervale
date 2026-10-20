# 004. GPU timestamps as public API

Part: framework (the runtime side is change 4, `../upstream/03-gpu-timestamps.md`). Phase R2.0.

## Need

`--gpu-timing` times every frame section with GPU timestamps recorded in MonoGame's own frame command buffer. WingSim's
`GpuTimestamps` reached the runtime's `MGG_TimestampPool_*` by reflection on the internal `GraphicsDevice.Handle` and
`NativeLibrary` exports. Every R2 effect is accepted on its GPU time (spec R2 §6), so the timestamps are a facility of the
framework.

## Cause

The framework was a NuGet package (issue 001).

## Change

`GpuTimestampPool : GraphicsResource` (`Graphics/GpuTimestampPool.cs`):
- `new GpuTimestampPool(GraphicsDevice, int count)`: a Vulkan timestamp query pool (`NotSupportedException` if the device
  cannot create one);
- `bool Reset(int first, int count)`: resets a range in the frame's command buffer; false inside a render pass or outside
  a frame (that frame goes untimed);
- `void Write(int index)`: `vkCmdWriteTimestamp` at the bottom of the pipe, at this point of the frame;
- `bool TryGetNanoseconds(int index, out ulong ns)`: without waiting; false until the frame completed;
- `Count`, `NanosecondsPerTick`; ranges are checked (`ArgumentOutOfRangeException`).

WingSim: `Engine/Graphics/GpuTimestamps.cs` is deleted; `GpuProfiler` (the per-section profiler, WingSim's) uses the pool.

## Test

The parity captures report the same GPU time per section before and after the change, within the run-to-run spread
(`tools/parity/compare.py`, numbers in research 12, "R2.0").
