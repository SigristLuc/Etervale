# monogame/issues: one document per change of the fork

Each document says what was needed or what went wrong (symptom or need), why (cause), what the fork changes (change)
and how the change is verified (test), for any of the fork's parts (`../runtime`, `../framework`, and the effect
processor `../pipeline`). Numbered in order of arrival.

The runtime's changes made before the fork took this form (changes 1–19: independent blend, render targets kept across
frames, GPU timestamps, vertex inputs by semantic, exact-match caches, read-back, viewport, uniform overflow, descriptor
sets, pixel-shader constants, resize mid-frame, frames-in-flight buffers, texture upload order, flush without acquire,
asynchronous frame capture, unreachable defaults, hidden window) are described in `../runtime/README.md` and, as notes
for the owner, in `../upstream/`.

| # | Document | Part | Phase |
|---|---|---|---|
| 001 | [framework-from-source](001-framework-from-source.md) | framework | R2.0 |
| 002 | [runtime-version](002-runtime-version.md) | runtime, framework | R2.0 |
| 003 | [frame-capture-api](003-frame-capture-api.md) | framework | R2.0 |
| 004 | [gpu-timestamps-api](004-gpu-timestamps-api.md) | framework | R2.0 |
| 005 | [compute-and-uav](005-compute-and-uav.md) | runtime, framework, processor | R2.0 |
| 006 | [structured-buffers](006-structured-buffers.md) | runtime, framework, processor | R2.0 |
| 007 | [mgcb-profiles](007-mgcb-profiles.md) | processor, framework | R2.0 |
| 008 | [shader-model-6.5](008-shader-model-6.5.md) | processor, framework | R2.0 |
| 009 | [audio-buffer-reuse](009-audio-buffer-reuse.md) | runtime | E2 |
| 010 | [sdl2-alsa-default-device](010-sdl2-alsa-default-device.md) | runtime (SDL2 patch, build) | E2 |
| 011 | [texture-load-binding](011-texture-load-binding.md) | processor | N1.1 |
| 012 | [pipeline-statistics](012-pipeline-statistics.md) | runtime, framework | N1.1 |
| 013 | [no-overwrite-uploads](013-no-overwrite-uploads.md) (Etervale's copy only) | runtime, framework | Etervale |
