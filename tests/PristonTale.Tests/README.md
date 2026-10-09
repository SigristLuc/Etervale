# Regression runner

Build in Release, with warnings treated as errors:

```bash
dotnet build tests/PristonTale.Tests -c Release -m:1 -warnaserror
dotnet run --project tests/PristonTale.Tests -c Release --no-build
```

The default runner uses synthetic data, original-protocol byte fixtures, a loopback TCP exchange and SQLite databases created and removed in the temporary directory.
It returns a nonzero exit code on failure. It is a console runner, not a test framework discovered by `dotnet test`.
No default case reads game assets, production databases or legacy reference directories.

Cases cover persisted attributes, session ownership, tick commands, validated movement reports, field transitions, invalid coordinates, AI integration,
inventory atomicity, original struct layouts, the packet cipher and replay window, and presence encoding through `WireRoundTrip`.
Presentation cases cover combat events/rates, pending actor removal, unused material tables, shadow-caster eligibility, CPU mesh preparation, image decoding and glTF opacity variants.

Optional synthetic CPU benchmark:

```bash
dotnet run --project tests/PristonTale.Tests -c Release --no-build -- --benchmark
```

This runs five sets of 128 players, 100 warmup ticks and 1,000 measured ticks, reporting median/p95/max and bytes per tick. No assets, monsters, GPU or database participate.

Optional Ricarten probes, only when reading those assets is authorized:

```bash
dotnet run --project tests/PristonTale.Tests -c Release --no-build -- --profile-ricarten /path/to/assets
xvfb-run -a dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --profile-stream /path/to/assets /path/to/compiled/Content
```

The first reports CPU parsing/collision/texture preparation. The second creates a graphics device and uses the real incremental renderer uploader at a fixed 60 Hz.
Compile the client first for the shader content. Its Content directory normally resides under `Client/PristonTale/bin/Release/net10.0/Content`.
The graphics probe clears the window instead of drawing the complete world: timings isolate publication and do not establish in-game frame time or GPU capacity.
It also reports batch/triangle counts and a digest of static vertex data plus skinned bounds/palettes; this is not a readback of WriteOnly GPU buffers or a visual comparison.

Movement cases additionally cover matching prediction/authority endpoints, idle stability and collision rejecting the final step.
Streaming policy covers the Forest 1 spawn prefetching Ricarten, speed-based lead distance and hysteresis.
Lamp cases compare supported SIMD kernels against an independent scalar oracle, including short inputs, tails and range boundaries.

Run the same suite with `DOTNET_EnableAVX2=0` and `DOTNET_EnableHWIntrinsic=0` to exercise narrower/no SIMD paths on the current JIT.
These are diagnostic runtime switches, not production settings or proof of execution on ARM hardware.

```bash
DOTNET_EnableAVX2=0 dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll
DOTNET_EnableHWIntrinsic=0 dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll
DOTNET_TieredCompilation=0 dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --benchmark-lamps
```

The lamp benchmark reports production dispatch, the previous scalar oracle and each available explicit kernel.
It uses 20,000 deterministic positions, 6/72/192 lights, warmup and five samples of 200,000 vertex evaluations; setup is outside the measured region.
The streaming probe uses the same 32-step/2 ms policy as the game; `--profile-stream-baseline` uses eight steps to isolate the previous limit.
Authentication and job-head coverage uses presentation probes in this runner.
HUD cases cover the original 800x600 layout at integer scales, its bottom/right anchors and hit rectangles, gauge cropping,
the panel slide steps, minimap clipping and the Sinimage color keys (black key for BMP, premultiplied TGA alpha).
The optional HUD probe renders the original in-game HUD scenarios, including scale 2 and 3, to PNG files:

```bash
dotnet tests/PristonTale.Tests/bin/Release/net10.0/PristonTale.Tests.dll --capture-hud assets Client/PristonTale/bin/Release/net10.0/Content /tmp/hud
```

It reads the Ricarten stage only for the authored minimap rectangle and accepts an optional background PNG as a fifth argument.
The login probe exercises Draw before Update with a preloaded shared atlas, a 1×1 viewport followed by restoration, disabled credentials, keyboard navigation, masking and scaled mouse hit tests.
The default suite covers zero/tiny viewport overlay bounds and all 416 normal job heads against `Fixtures/job-head-source.json`, extracted from PTClassic `playmodel.h` rows 0..3.
# Regressões de terreno e transição

O runner inclui pisos sobrepostos, rampas, buracos, degraus altos, frames longos e corrida entre campos pela autoridade compartilhada.
Também verifica colisão da câmera entre campos, contato físico, câmera acima de portal/poste, foco em várias frequências, reposicionamento, botão do meio e rotação pelas bordas com captura pela UI/foco da janela.
O probe opcional lê somente o stage original de Ricarten, sem GPU, para verificar pisos sobrepostos e câmeras altas com o foco encoberto próximos ao portão:

```bash
dotnet run --project tests/PristonTale.Tests -c Release --no-build -- --inspect-ricarten-ground assets
```
