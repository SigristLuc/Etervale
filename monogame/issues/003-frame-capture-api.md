# 003. Asynchronous frame capture as public API

Part: framework (the runtime side is change 17, `../upstream/16-async-frame-capture.md`). Phase R2.0.

## Need

`--check-glitches` reads the presented frames without synchronising, so the races between the CPU and frames still on
the GPU show (research 12, "Corridas entre quadros em voo"). It reached the runtime's `MGG_FrameCapture_*` from
WingSim's `AsyncFrameCapture`, which read the internal `GraphicsDevice.Handle` by reflection and bound the exports with
`NativeLibrary`. Reading the presented frames is a generic GPU facility (the QTG latency measurement of spec D and the
pilot-stimulus latency of spec E6 need it too), so it belongs to the framework.

## Cause

The framework was a NuGet package (issue 001).

## Change

`GraphicsDevice` (`Graphics/GraphicsDevice.FrameCapture.cs`):
- `bool BeginFrameCapture(int downscale, int maxQueued)`: every `Present` copies the image it presents (downscaled by a
  blit) into a host buffer of its frame slot, inside the frame's own command buffer; false when the swapchain cannot be
  read;
- `bool TryPopFrame(Span<byte> rgba, out PresentedFrame frame)`: the oldest frame not read yet (RGBA8); false when none is
  ready, or when the span is too small (nothing is removed, `frame` gives the size);
- `ulong DroppedCapturedFrames`, `bool IsCapturingFrames`, `void EndFrameCapture()`.
- `PresentedFrame`: `Width`, `Height`, `Number` (the device's frame counter), `Drawn` (false when nothing was drawn into
  the presented image), `ByteCount`.

WingSim: `Engine/Graphics/AsyncFrameCapture.cs` is deleted; `Game/WingSimGame.GlitchCheck.cs` calls the device. What is
WingSim's (the glitch criteria of `FrameGlitchCheck`, the dumps) stays in WingSim. No reflection is left.

## Test

`tools/parity/glitches.sh` (the three regression scenes of `--check-glitches`) passes as before the change (numbers in
research 12, "R2.0").
