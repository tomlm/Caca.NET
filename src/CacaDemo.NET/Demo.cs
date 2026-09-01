/*
 *  CacaDemo.NET  various demo effects for libcaca, ported to .NET
 *
 *  Ported from cacademo.c, which is
 *  Copyright (c) 1998 Michele Bini <mibin@tin.it>
 *                2003-2006 Jean-Yves Lamoureux <jylam@lnxscene.org>
 *                2004-2012 Sam Hocevar <sam@hocevar.net>
 *
 *  This program is free software. It comes without any warranty, to
 *  the extent permitted by applicable law. You can redistribute it
 *  and/or modify it under the terms of the Do What the Fuck You Want
 *  to Public License, Version 2, as published by Sam Hocevar. See
 *  http://www.wtfpl.net/ for more details.
 */

using Caca;

namespace CacaDemo;

/// <summary>
/// One visual effect. This replaces the C original's single
/// <c>fn(enum action, caca_canvas_t *)</c> entry point: each of the five
/// actions becomes a method.
/// </summary>
public interface IDemo
{
    /// <summary>Build lookup tables. Called once, for every demo, at startup.</summary>
    void Prepare(Canvas cv);

    /// <summary>Allocate per-run buffers. Called each time the demo comes on screen.</summary>
    void Init(Canvas cv);

    /// <summary>Advance the effect by one frame.</summary>
    void Update(Canvas cv, int frame);

    /// <summary>Draw the current state onto <paramref name="cv"/>.</summary>
    void Render(Canvas cv);

    /// <summary>Release what <see cref="Init"/> allocated.</summary>
    void Free();
}

/// <summary>
/// Shared constants for the dither-based demos.
/// </summary>
internal static class Sizes
{
    public const int XSIZ = 256;
    public const int YSIZ = 256;
}
