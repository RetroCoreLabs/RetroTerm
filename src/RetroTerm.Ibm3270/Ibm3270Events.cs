//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

namespace RetroTerm.Ibm3270
{
    /// <summary>
    /// A 3270 message for the person: a record the model rejects, or a key not built yet.
    /// </summary>
    /// <param name="text">
    /// What happened, in words, with the manual page where there is one.
    /// </param>
    public delegate void Ibm3270Message(string text);

    /// <summary>
    /// The host asked for the alarm (the WCC sound-alarm bit, GA23-0059-4 p. 3-3).
    /// </summary>
    public delegate void Ibm3270Alarm();
}
