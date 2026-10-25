// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the platform exports the runtime fork (monogame/runtime) adds to the native runtime. Each group has a
// change document in monogame/issues.

using System;
using System.Runtime.InteropServices;

namespace MonoGame.Interop;

internal static unsafe partial class MGP
{
    #region Clipboard (Etervale fork, issue 014)

    [DllImport(MonoGameNativeDLL, EntryPoint = "MGP_Platform_GetClipboardText", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr _Platform_GetClipboardText();

    /// <summary>The clipboard's text, or null when it holds none.</summary>
    public static string Platform_GetClipboardText()
    {
        var text = _Platform_GetClipboardText();
        if (text == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStringUTF8(text);
        }
        finally
        {
            _Platform_Free(text);
        }
    }

    #endregion
}
