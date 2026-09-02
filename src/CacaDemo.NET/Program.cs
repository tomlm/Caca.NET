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

using System.Diagnostics;
using Caca;
using CacaDemo.Demos;

namespace CacaDemo;

internal static class Program
{
    /// <summary>
    /// One effect, the <c>--switch</c> that selects it, and whether the
    /// default rotation includes it.
    /// </summary>
    private sealed record DemoEntry(string Name, IDemo Demo, bool InRotation);

    /// <summary>
    /// Counts presented frames and republishes a rate a few times a second.
    /// </summary>
    /// <remarks>
    /// <para>Averaged over a window rather than derived from the last frame gap: at fifty frames
    /// a second a single gap is a handful of milliseconds, so an instantaneous reading is mostly
    /// scheduler noise and unreadable besides -- the digits change faster than an eye can follow.</para>
    /// <para>The window is wall time, not a frame count, so the reading keeps updating at a
    /// steady cadence even when the rate collapses. Counting frames instead would make a terminal
    /// that had fallen to two frames a second update its own bad news every ten seconds.</para>
    /// </remarks>
    private sealed class FpsMeter
    {
        /// <summary>How much wall time each reading averages over.</summary>
        private const double WindowSeconds = 0.5;

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _windowStart;
        private int _frames;
        private double _rate;

        /// <summary>Records one presented frame.</summary>
        public void Tick()
        {
            _frames++;

            long now = _clock.ElapsedTicks;
            double elapsed = (double)(now - _windowStart) / Stopwatch.Frequency;

            if (elapsed < WindowSeconds)
                return;

            _rate = _frames / elapsed;
            _frames = 0;
            _windowStart = now;
        }

        /// <summary>
        /// The reading, at a fixed width so a shrinking number cannot leave a digit of the
        /// previous one behind on a canvas the demo did not happen to repaint.
        /// </summary>
        public string Text => _rate > 0
            ? string.Format(" {0,6:0.0} fps ", _rate)
            : "   --.- fps ";
    }

    private static readonly DemoEntry[] All =
    [
        new("plasma", new Plasma(), true),
        new("metaballs", new Metaballs(), true),
        new("moire", new Moire(), true),
        /* Langton's ants is present but, as upstream, left out of the rotation. */
        new("langton", new Langton(), false),
        new("matrix", new Matrix(), true),
        new("rotozoom", new Rotozoom(), true),
    ];

    /// <summary>Shown over the first hundred frames, two columns in from the
    /// right as upstream places it.</summary>
    private const string Banner = " -=[ Powered by Caca.NET ]=- ";

    private const int TransitionFrames = 40;

    private static int DemoFrames() => CacaNet.Rand(500, 1000);

    private static int Main(string[] args)
    {
        if (!ParseArgs(args, out IDemo[] fn, out bool handled, out bool showFps))
            return handled ? 0 : 1;

        FpsMeter? fps = showFps ? new FpsMeter() : null;

        int frame = 0;
        int next = -1;
        bool paused = false;
        /* Which way through the rotation the next transition steps. */
        int step = 1;
        /* A lone effect never transitions: it just runs until you quit. */
        bool rotates = fn.Length > 1;
        int nextTransition = rotates ? DemoFrames() : int.MaxValue;
        int tmode = CacaNet.Rand(0, Transitions.Count);

        /* Set up two canvases, a mask, and attach a display to the front one */
        Canvas frontcv = new();
        Canvas backcv = new();
        Canvas mask = new();

        using Display dp = new(frontcv);

        backcv.Resize(frontcv.Width, frontcv.Height);
        mask.Resize(frontcv.Width, frontcv.Height);

        dp.DisplayTime = 20000;
        dp.Title = "cacademo";

        /* Initialise all demos' lookup tables */
        foreach (IDemo demo in fn)
            demo.Prepare(frontcv);

        /* Start at the first demo; the rotation runs in order from there */
        int current = 0;
        fn[current].Init(frontcv);

        while (true)
        {
            /* Handle events */
            while (true)
            {
                Event ev = dp.GetEvent(EventType.KeyPress | EventType.Quit);

                if (ev.Type == EventType.None)
                    break;

                if (ev.Type == EventType.Quit)
                    goto end;

                switch (ev.KeyCh)
                {
                    case (int)EventKey.Escape:
                    case (int)EventKey.CtrlC:
                    case (int)EventKey.CtrlZ:
                        goto end;
                    case ' ':
                        paused = !paused;
                        break;
                    case '\r':
                    case (int)EventKey.Right:
                        /* Skip forward to the next effect */
                        if (rotates && next == -1)
                        {
                            step = 1;
                            nextTransition = frame;
                        }
                        break;
                    case (int)EventKey.Left:
                        /* Skip back to the previous one */
                        if (rotates && next == -1)
                        {
                            step = -1;
                            nextTransition = frame;
                        }
                        break;
                }
            }

            /* Resize the spare canvas, just in case the main one changed */
            backcv.Resize(frontcv.Width, frontcv.Height);
            mask.Resize(frontcv.Width, frontcv.Height);

            if (!paused)
            {
                /* Update demo's data */
                fn[current].Update(frontcv, frame);

                /* Handle transitions */
                if (frame == nextTransition)
                {
                    next = ((current + step) % fn.Length + fn.Length) % fn.Length;
                    /* Left and Right steer one transition; the timer runs forward. */
                    step = 1;
                    fn[next].Init(backcv);
                }
                else if (frame == nextTransition + TransitionFrames)
                {
                    fn[current].Free();
                    current = next;
                    next = -1;
                    nextTransition = frame + DemoFrames();
                    tmode = CacaNet.Rand(0, Transitions.Count);
                }

                if (next != -1)
                    fn[next].Update(backcv, frame);

                frame++;
            }

            /* Render main demo's canvas */
            fn[current].Render(frontcv);

            /* If a transition is on its way, render it */
            if (next != -1)
            {
                fn[next].Render(backcv);
                mask.SetColorAnsi(AnsiColor.LightGray, AnsiColor.Black);
                mask.Clear();
                mask.SetColorAnsi(AnsiColor.White, AnsiColor.White);
                Transitions.Draw(mask, tmode,
                                 100 * (frame - nextTransition) / TransitionFrames);
                frontcv.Blit(0, 0, backcv, mask);
            }

            frontcv.SetColorAnsi(AnsiColor.White, AnsiColor.Blue);
            if (frame < 100)
            {
                frontcv.PutStr(frontcv.Width - Banner.Length - 2,
                               frontcv.Height - 2, Banner);
            }

            /* Counted here rather than after Refresh, because Refresh is where the frame
             * interval is waited out -- so this counts frames actually PRESENTED, at the rate
             * they reach the terminal. A terminal that cannot keep up drags the reading below
             * the rate DisplayTime asks for, which is the whole point of showing it. */
            if (fps is not null)
            {
                fps.Tick();

                string reading = fps.Text;
                /* Top right, mirroring the banner's inset at the bottom, so the two never
                 * collide during the first hundred frames. */
                frontcv.SetColorAnsi(AnsiColor.Black, AnsiColor.White);
                frontcv.PutStr(frontcv.Width - reading.Length - 2, 1, reading);
            }

            dp.Refresh();
        }

    end:
        if (next != -1)
            fn[next].Free();
        fn[current].Free();

        return 0;
    }

    /// <summary>
    /// Turns the command line into the set of effects to run. Naming effects
    /// (<c>--plasma</c>, <c>--matrix</c>, ...) restricts the rotation to those,
    /// in the order given; a single one runs standalone, with no transitions.
    /// </summary>
    /// <returns><c>false</c> if the program should exit without running, with
    /// <paramref name="handled"/> telling apart <c>--help</c> from a bad
    /// argument.</returns>
    private static bool ParseArgs(string[] args, out IDemo[] fn, out bool handled, out bool showFps)
    {
        fn = [];
        handled = false;
        showFps = false;

        List<IDemo> selected = [];

        foreach (string arg in args)
        {
            string name = arg.TrimStart('-');

            if (arg is "-h" or "--help" or "-?" or "/?")
            {
                Usage(Console.Out);
                handled = true;
                return false;
            }

            if (arg.StartsWith('-') && name.Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                foreach (DemoEntry entry in All)
                    Console.WriteLine(entry.Name);
                handled = true;
                return false;
            }

            /* Answered before the effect lookup below, which would otherwise reject it as an
             * unrecognised effect name. Deliberately not added to `selected`: asking for the
             * counter says nothing about WHICH effects to run, so `--fps` on its own still
             * gets the whole rotation. */
            if (arg.StartsWith('-') && name.Equals("fps", StringComparison.OrdinalIgnoreCase))
            {
                showFps = true;
                continue;
            }

            DemoEntry? match = arg.StartsWith('-')
                ? Array.Find(All, e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                : null;

            if (match is null)
            {
                Console.Error.WriteLine($"cacademo: unrecognised argument '{arg}'");
                Usage(Console.Error);
                return false;
            }

            if (!selected.Contains(match.Demo))
                selected.Add(match.Demo);
        }

        /* Nothing named: the full rotation. */
        if (selected.Count == 0)
        {
            foreach (DemoEntry entry in All)
            {
                if (entry.InRotation)
                    selected.Add(entry.Demo);
            }
        }

        fn = [.. selected];
        return true;
    }

    private static void Usage(TextWriter w)
    {
        w.WriteLine("Usage: cacademo [OPTION]...");
        w.WriteLine();
        w.WriteLine("Run libcaca demo effects. With no effect named, cycles through the whole");
        w.WriteLine("rotation in order. Name one effect to run it standalone, or name several");
        w.WriteLine("to cycle through just those, in the order given.");
        w.WriteLine();
        w.WriteLine("Effects:");
        foreach (DemoEntry entry in All)
        {
            string note = entry.InRotation ? "" : "  (not in the default rotation)";
            w.WriteLine($"  --{entry.Name}{note}");
        }
        w.WriteLine();
        w.WriteLine("  --fps       show the achieved frame rate in the top right corner");
        w.WriteLine("  --list      list the effect names, one per line");
        w.WriteLine("  -h, --help  show this help");
    }
}
