# 012. Pipeline statistics (registers, spills) from the driver

Parts: runtime, framework. Phase N1.1 (research 14 N1, the clouds' cost).

## Need

The clouds' march shader cost twice its predecessor per texel. GPU timestamps say how long a pass takes, not why: the
number of registers a shader needs decides how many warps hide a texture fetch (occupancy), and a shader that needs more
than the driver gives it spills to memory. Without these numbers each change to the shader was a guess.

## Cause

MonoGame has no access to the driver's view of a compiled pipeline. Vulkan exposes it through
`VK_KHR_pipeline_executable_properties`: per executable (shader stage) of a pipeline, the driver's statistics (NVIDIA:
register count, binary size, stack size, local memory, inputs and outputs), when the pipeline was created with
`VK_PIPELINE_CREATE_CAPTURE_STATISTICS_BIT_KHR` and the device feature `pipelineExecutableInfo` is enabled.

## Change

**Runtime** (`src/vulkan/MGG_Vulkan.cpp`):
- With `MG_PIPELINE_STATISTICS=1` in the environment at device creation and the extension present, the extension and its
  feature are enabled and every graphics and compute pipeline is created with the capture flag (off by default: capturing
  may slow pipeline creation). The last bound graphics and compute pipelines are kept.
- `MGG_GraphicsDevice_GetPipelineStatistics(device, compute, buffer, capacity)`: the statistics of the last bound graphics
  (`compute` 0) or compute (1) pipeline as UTF-8 lines `executable<TAB>statistic<TAB>value`; returns the length, 0 before
  any pipeline, -1 when the statistics are off.
- Interface version 3 (`MGG_WingSim_RuntimeVersion`, issue 002).

**Framework:** `GraphicsDevice.GetPipelineStatistics(bool compute)` returns the list of `PipelineStatistic` (executable,
name, value), or null when off (`Graphics/GraphicsDevice.PipelineStatistics.cs`).

**WingSim:** `--pipeline-statistics` sets the variable and prints, once per technique, the statistics of every cloud pass
(`CloudRenderer.PipelineStatistics`).

## Test

GTX 1050 Ti, driver 580.178: the cloud passes report their statistics (`Clouds March, FS: Register Count = 124`,
`Stack Size = 1536`; `Reconstruct` 56 registers, `BodySlices` 40). They guided the march's changes: the per-segment
sky-table haze held 28 registers at the march's peak (124 → 96 once the air's source function is taken once per ray).
Without the variable the runtime creates its pipelines as before and the call returns -1.
