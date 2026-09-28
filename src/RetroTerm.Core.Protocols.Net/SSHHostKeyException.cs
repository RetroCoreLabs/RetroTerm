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

