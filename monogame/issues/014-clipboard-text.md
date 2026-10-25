# 014. The clipboard's text

Parts: runtime and framework.

## Need

The game's chat input takes typed characters only. Item names, coordinates and game-master commands copied from
outside the game had to be typed again by hand.

## Cause

MonoGame's Native platform has no clipboard access: SDL2 has one (`SDL_GetClipboardText`), but the runtime exports no
function that reaches it, and SDL2 is linked statically into the runtime.

## Change

**Runtime** (`src/sdl/MGP_sdl.cpp`): `MGP_Platform_GetClipboardText()` returns the clipboard's text as UTF-8, allocated
by SDL and released with `MGP_Platform_Free`, or null when the clipboard holds no text (or only an empty string).
Interface version 5 (`MGG_Etervale_RuntimeVersion`, issue 002).

**Framework:** `Microsoft.Xna.Framework.Input.Clipboard.GetText()` returns the text or null (`Input/Clipboard.cs`, the
P/Invoke in `Platform/Native/Platform.Interop.Etervale.cs`). It is meant for the game thread after the window exists; on
X11 SDL asks the selection's owner for the text, which can take a moment.

**Application:** Ctrl+V in the chat input pastes the clipboard's first line.

## Test

- The regression runner checks the chat input's paste rules (first line, tabs as spaces, refused characters skipped,
  stop at the input limit).
- Windows 11, RTX 3070 Ti: text copied in another program and pasted with Ctrl+V into the chat input appears in it
  (checked by hand).
