using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV.TDV2200Validation;

/// <summary>
/// Base class for all TDV-2200 validation tests
/// Provides spec reference lookup, RetroCore comparison, and assertion helpers
/// </summary>
public abstract class TDV2200ValidationTestBase : IDisposable
{
    protected readonly TDV2200Emulator Emulator;
    protected readonly TDV2200SpecDatabase SpecDatabase;
    protected readonly List<string> Responses = new();
    protected readonly InMemoryConnection Connection;

    protected TDV2200ValidationTestBase()
    {
        Emulator = new TDV2200Emulator(80, 24);
        Emulator.OnResponseReady += response => Responses.Add(response);

        // Initialize spec database
        var specDir = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..", "spec", "TDV2200");
        SpecDatabase = new TDV2200SpecDatabase(specDir);

        Connection = new InMemoryConnection();
    }

    /// <summary>
    /// Sends a sequence to the emulator and processes it
    /// For raw byte sequences, use SendBytes() instead
    /// </summary>
    protected void SendSequence(string sequence)
    {
        var bytes = Encoding.UTF8.GetBytes(sequence);
        Emulator.ProcessInput(bytes);
    }

    /// <summary>
    /// Sends raw bytes to the emulator (for escape sequences and control characters)
    /// This is the correct way to send terminal control sequences - as raw bytes, not encoded strings
    /// </summary>
    protected void SendBytes(params byte[] bytes)
    {
        Emulator.ProcessInput(bytes);
    }

    /// <summary>
    /// Asserts that a response matches the expected format from spec
    /// </summary>
    protected void AssertResponseMatchesSpec(string query, string actualResponse)
    {
        var expectedResponse = SpecDatabase.GetExpectedResponse(query);
        if (expectedResponse != null)
        {
            Assert.Equal(expectedResponse, actualResponse);
        }
        else
        {
            // If no spec found, at least verify it's a valid response
            Assert.NotNull(actualResponse);
            Assert.NotEmpty(actualResponse);
        }
    }

    /// <summary>
    /// Asserts that an escape sequence matches spec
    /// </summary>
    protected void AssertEscapeSequenceMatchesSpec(string sequence, string function)
    {
        var spec = SpecDatabase.FindEscapeSequence(sequence);
        if (spec != null)
        {
            Assert.Equal(function, spec.Function);
        }
    }

    /// <summary>
    /// Gets cell at position (for display validation)
    /// </summary>
    protected TerminalCell? GetCell(int row, int col)
    {
        var buffer = Emulator.GetBuffer();
        if (row >= 0 && row < Emulator.Height && col >= 0 && col < Emulator.Width)
        {
            return buffer[row, col];
        }
        return null;
    }

    /// <summary>
    /// Gets cursor position
    /// </summary>
    protected (int row, int col) GetCursorPosition()
    {
        var cursor = Emulator.GetCursor();
        return (cursor.Row, cursor.Column);
    }

    /// <summary>
    /// Clears all responses
    /// </summary>
    protected void ClearResponses()
    {
        Responses.Clear();
    }

    /// <summary>
    /// Gets the last response
    /// </summary>
    protected string? GetLastResponse()
    {
        return Responses.LastOrDefault();
    }

    public virtual void Dispose()
    {
        Connection?.Dispose();
    }
}

