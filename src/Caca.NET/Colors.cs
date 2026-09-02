/*
 *  Caca.NET      a managed port of libcaca's canvas, dithering and terminal output
 *  Ported from libcaca, Copyright (c) 2002-2018 Sam Hocevar <sam@hocevar.net>
 *
 *  This library is free software. It comes without any warranty, to
 *  the extent permitted by applicable law. You can redistribute it
 *  and/or modify it under the terms of the Do What the Fuck You Want
 *  to Public License, Version 2, as published by Sam Hocevar. See
 *  http://www.wtfpl.net/ for more details.
 */

namespace Caca;

/// <summary>The sixteen ANSI colours, plus libcaca's two special values.</summary>
public enum AnsiColor
{
    Black = 0x00,
    Blue = 0x01,
    Green = 0x02,
    Cyan = 0x03,
    Red = 0x04,
    Magenta = 0x05,
    Brown = 0x06,
    LightGray = 0x07,
    DarkGray = 0x08,
    LightBlue = 0x09,
    LightGreen = 0x0a,
    LightCyan = 0x0b,
    LightRed = 0x0c,
    LightMagenta = 0x0d,
    Yellow = 0x0e,
    White = 0x0f,

    /// <summary>The output driver's default colour.</summary>
    Default = 0x10,

    /// <summary>The transparent colour.</summary>
    Transparent = 0x20,
}

/// <summary>Text style bits, stored in the low nibble of an attribute.</summary>
[Flags]
public enum AnsiStyle
{
    None = 0x00,
    Bold = 0x01,
    Italics = 0x02,
    Underline = 0x04,
    Blink = 0x08,
}
