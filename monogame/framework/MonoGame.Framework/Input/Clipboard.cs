// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the system clipboard's text (monogame/issues/014-clipboard-text.md).

using MonoGame.Interop;

namespace Microsoft.Xna.Framework.Input
{
    /// <summary>Reads the system clipboard.</summary>
    public static class Clipboard
    {
        /// <summary>
        /// The clipboard's text, or null when it holds none. Call it on the game thread after the window exists; on X11
        /// the owner of the selection is asked for it, which can take a moment.
        /// </summary>
        public static string GetText() => MGP.Platform_GetClipboardText();
    }
}
