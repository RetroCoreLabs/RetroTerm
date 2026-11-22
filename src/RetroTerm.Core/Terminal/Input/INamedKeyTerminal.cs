//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;

namespace RetroTerm.Core.Terminal.Input
{
    /// <summary>
    /// What happened when a key was pressed by its name.
    /// </summary>
    public enum NamedKeyResult
    {
        /// <summary>
        /// The key was pressed.
        /// </summary>
        Pressed,

        /// <summary>
        /// This terminal has no key with that name.
        /// </summary>
        UnknownName,

        /// <summary>
        /// The key exists on the real terminal but RetroTerm does not do it yet. Nothing was
        /// sent; the caller must report it, never pretend it worked.
        /// </summary>
        NotBuilt,
    }

    /// <summary>
    /// A terminal whose keys can be pressed by name - by SENDKEY in a script, by MCP, and by the
    /// on-screen keyboard. ONE table per terminal, so the three can never disagree (the
    /// two-surface trap in CLAUDE.md).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A key's full name is the family, a dot and the key: IBM.PF3, TDV.HJELP, VT.PF1. The family
    /// stops a script written for one terminal from pressing a same-named key on another.
    /// </para>
    /// <para>
    /// The TDV terminals answer key names through TDV2200KeyRegistry instead (it existed first,
    /// and the TDV keyboard tests guard it), but they use the same family rule.
    /// </para>
    /// </remarks>
    public interface INamedKeyTerminal
    {
        /// <summary>
        /// The family prefix, without the dot: "IBM" for the 3270.
        /// </summary>
        string KeyFamily { get; }

        /// <summary>
        /// Every key name this terminal has, without the family prefix, in upper case.
        /// </summary>
        IReadOnlyList<string> KeyNames { get; }

        /// <summary>
        /// Presses the key with this name.
        /// </summary>
        /// <param name="name">
        /// The key name without the family prefix, any case.
        /// </param>
        /// <param name="notBuiltReason">
        /// When the result is NotBuilt, what is missing; otherwise null.
        /// </param>
        /// <returns>
        /// What happened.
        /// </returns>
        NamedKeyResult PressNamedKey(string name, out string? notBuiltReason);
    }
}
