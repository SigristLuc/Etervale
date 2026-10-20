# 009. A streaming voice's free buffers are reused one at a time

Part: runtime. Workstream E2 (audio).

## Symptom

WingSim's mixer (`src/WingSim.Audio`) streams the whole simulator's sound through one `DynamicSoundEffectInstance`: a
block of 10 ms (1 920 bytes of 16-bit stereo) every 10 ms, three blocks queued, from an audio thread
(`src/WingSim.Engine/Audio/AudioOutput.cs`). Every block goes through `MGA_Voice_AppendBuffer`
(`runtime/src/faudio/MGA_faudio.cpp`), which takes a free copy buffer from the system's list (`freeRawBuffers`, filled
by `OnBufferEnd`) or allocates one. When three or more free buffers fit, the function used one and **lost the
others**: they left the free list and were never freed or used again (a native leak of one block per lost buffer, and
new allocations afterwards).

## Cause

The search loop takes a fitting buffer and erases it from the vector but does not stop: the next index skips the
element that moved into the erased slot, and a later fitting buffer overwrites `raw`. With the list `[A, B, C]` it
takes `A`, then `C`, and `A` is gone. The list holds three or more buffers whenever the voice finished that many before
the next append, which an audio thread that sleeps a few milliseconds between appends meets during load hitches.

## Change

`break` after the buffer is taken (`WingSim fork (issue 009)` marker). `regenpatch.sh` now also records
`faudio/MGA_faudio.cpp` in `monogame-v3.8.6.patch`. No exported function changes: the interface version stays 2.

## Test

- By reading: with the `break`, the buffer removed from the list is the one submitted, and `OnBufferEnd` returns it.
- Correction (2026-10-16): this section said the game streamed its sound for minutes on the rebuilt runtime. That was
  wrong: every E2 check then was offline (`--audio-capture` renders on the simulation clock, no device), and the real
  device path crashed on ALSA (issue 010). Measured since, with the device open (`--check-audio-device`, issue 010):
  10 s on SDL's default driver, PipeWire, PulseAudio and ALSA, 1000–1001 blocks of 10 ms each, 0 underruns, and three
  minutes on PipeWire and on ALSA (`docs/research/14-specs/E2-audio.md`).
