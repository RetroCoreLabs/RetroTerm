//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;

namespace RetroTerm.Core.Protocols.Net;

/// <summary>
/// Exception thrown when SSH host key validation fails
/// Used to interrupt the connection flow and allow UI to handle host key confirmation
/// </summary>
public class SSHHostKeyException : Exception
{
    public HostKeyValidationResult Result { get; }

    public SSHHostKeyException(HostKeyValidationResult result)
        : base($"SSH host key verification failed: {result.Message}")
    {
        Result = result;
    }
}

