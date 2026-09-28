using System;
using System.Collections.Generic;
using System.Text;

namespace RetroTerm.Core.Terminal.Emulators.TDV.Components;

/// <summary>
/// TDV2215 DCS Handler Feature - Reusable component for any TDV terminal
/// Handles PUSH-key and PROGRAM-key programming via DCS sequences
/// </summary>
public class TDVDCSHandlerFeature
{
    private readonly List<byte> _dcsBuffer;
    private bool _isReceivingDCS;

    public TDVDCSHandlerFeature()
    {
        _dcsBuffer = new List<byte>();
        _isReceivingDCS = false;
    }

    /// <summary>
    /// Gets whether DCS sequence is being received
    /// </summary>
    public bool IsReceivingDCS => _isReceivingDCS;

    /// <summary>
    /// Start receiving a DCS sequence. Called when the parser has finished the DCS
    /// introducer.
    /// </summary>
    public void StartDCS()
    {
        _isReceivingDCS = true;
        _dcsBuffer.Clear();
    }

    /// <summary>
    /// Adds a chunk of DCS payload. The parser delivers the payload as a stream, so this
    /// may be called many times for one sequence.
    /// </summary>
    /// <param name="data">
    /// Payload bytes; copied, so the caller's span may be reused.
    /// </param>
    /// <remarks>
    /// This class used to scan the bytes itself looking for ESC/ST to decide where the
    /// sequence ended. That is the parser's job and it is now done there properly, so this
    /// only accumulates - no terminator handling, no chance of the two disagreeing.
    /// </remarks>
    public void AppendData(ReadOnlySpan<byte> data)
    {
        if (!_isReceivingDCS)
        {
            return;
        }

        for (int i = 0; i < data.Length; i++)
        {
            _dcsBuffer.Add(data[i]);
        }
    }

    /// <summary>
    /// Ends the current DCS and acts on whatever was collected.
    /// </summary>
    /// <param name="emulator">
    /// The emulator to apply PUSH/PROGRAM key changes to.
    /// </param>
    public void EndDCS(TDVEmulatorBase emulator)
    {
        if (!_isReceivingDCS)
        {
            return;
        }

        ProcessDCSSequence(emulator);
        _dcsBuffer.Clear();
        _isReceivingDCS = false;
    }

    /// <summary>
    /// Process the completed DCS sequence
    /// </summary>
    private void ProcessDCSSequence(TDVEmulatorBase emulator)
    {
        if (_dcsBuffer.Count < 2) return;

        // Check for PUSH-key or PROGRAM-key sequences
        var sequence = Encoding.ASCII.GetString(_dcsBuffer.ToArray());

        if (sequence.StartsWith("PUSH"))
        {
            // Handle PUSH-key programming
            HandlePUSHKeySequence(sequence, emulator);
        }
        else if (sequence.StartsWith("PROGRAM"))
        {
            // Handle PROGRAM-key loading
            HandlePROGRAMKeySequence(sequence, emulator);
        }
    }

    /// <summary>
    /// Handle PUSH-key programming sequence
    /// </summary>
    private void HandlePUSHKeySequence(string sequence, TDVEmulatorBase emulator)
    {
        // Parse PUSH-key programming sequence
        // Format: PUSH<key><data>
        if (sequence.Length > 4)
        {
            var key = sequence[4];
            var data = sequence.Substring(5);

            // Store the key programming data using emulator's PushKeys
            if (char.IsDigit(key))
            {
                int keyNumber = key - '0';
                emulator.PushKeys.ProgramKey(keyNumber, data);
            }
        }
    }

    /// <summary>
    /// Handle PROGRAM-key loading sequence
    /// </summary>
    private void HandlePROGRAMKeySequence(string sequence, TDVEmulatorBase emulator)
    {
        // Parse PROGRAM-key loading sequence
        // Format: PROGRAM<key>
        if (sequence.Length > 7)
        {
            var key = sequence[7];

            // Load the programmed key data using emulator's PushKeys
            if (char.IsDigit(key))
            {
                int keyNumber = key - '0';
                if (emulator.PushKeys.IsKeyProgrammed(keyNumber))
                {
                    var sequenceData = emulator.PushKeys.GetKeySequence(keyNumber);
                    if (sequenceData != null)
                    {
                        // Send the programmed sequence back
                        var bytes = Encoding.UTF8.GetBytes(sequenceData);
                        emulator.ProcessInput(bytes);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Reset DCS handler to initial state
    /// </summary>
    public void Reset()
    {
        _dcsBuffer.Clear();
        _isReceivingDCS = false;
    }

    /// <summary>
    /// Process the DCS sequence and return result
    /// </summary>
    public string ProcessDCS()
    {
        if (_dcsBuffer.Count < 2) return string.Empty;

        var sequence = Encoding.ASCII.GetString(_dcsBuffer.ToArray());
        _dcsBuffer.Clear();
        _isReceivingDCS = false;

        return sequence;
    }
}

