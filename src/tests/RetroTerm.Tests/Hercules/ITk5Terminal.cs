//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Threading.Tasks;

namespace RetroTerm.Tests.Hercules
{
    /// <summary>
    /// A test of one screen: true when the step it waits for has happened.
    /// </summary>
    internal delegate bool Tk5ScreenTest(Tk5Screen screen);

    /// <summary>
    /// A 3270 terminal a scenario drives: RetroTerm's own, or s3270 as the reference. The same
    /// steps run on both so their screens can be compared step by step.
    /// </summary>
    internal interface ITk5Terminal : IAsyncDisposable
    {
        /// <summary>
        /// The name used in a difference report.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Types text at the cursor, as a person would.
        /// </summary>
        Task TypeAsync(string text);

        /// <summary>
        /// Presses a 3270 key by its RetroTerm name: ENTER, CLEAR, RESET, PF1-PF24, PA1-PA3, TAB.
        /// </summary>
        Task PressAsync(string key);

        /// <summary>
        /// Waits until the keyboard is unlocked and the screen passes the test, or fails with the
        /// screen it last saw when the deadline passes.
        /// </summary>
        Task<Tk5Screen> WaitUntilAsync(Tk5ScreenTest done, string what, int timeoutMs);

        /// <summary>
        /// Reads the screen as it is now.
        /// </summary>
        Task<Tk5Screen> ReadAsync();
    }
}
