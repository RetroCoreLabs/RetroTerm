using System;
using System.Collections.Generic;

namespace RetroTerm.Core.Terminal.Emulators.TDV;

/// <summary>
/// Manages programmable PUSH keys for TDV terminals
/// </summary>
public class TDVPushKeys
{
    private readonly Dictionary<int, string> _programmedKeys;
    private const int MaxKeys = 24; // TDV supports up to 24 PUSH keys

    public TDVPushKeys()
    {
        _programmedKeys = new Dictionary<int, string>();
    }

    /// <summary>
    /// Programs a PUSH key with a sequence
    /// </summary>
    public void ProgramKey(int keyNumber, string sequence)
    {
        if (keyNumber < 1 || keyNumber > MaxKeys)
        {
            throw new ArgumentOutOfRangeException(nameof(keyNumber), $"Key number must be between 1 and {MaxKeys}");
        }

        if (string.IsNullOrEmpty(sequence))
        {
            _programmedKeys.Remove(keyNumber);
        }
        else
        {
            _programmedKeys[keyNumber] = sequence;
        }
    }

    /// <summary>
    /// Gets the sequence for a programmed key
    /// </summary>
    public string GetKeySequence(int keyNumber)
    {
        return _programmedKeys.TryGetValue(keyNumber, out var sequence) ? sequence : string.Empty;
    }

    /// <summary>
    /// Checks if a key is programmed
    /// </summary>
    public bool IsKeyProgrammed(int keyNumber)
    {
        return _programmedKeys.ContainsKey(keyNumber);
    }

    /// <summary>
    /// Clears a specific key
    /// </summary>
    public void ClearKey(int keyNumber)
    {
        _programmedKeys.Remove(keyNumber);
    }

    /// <summary>
    /// Clears all programmed keys
    /// </summary>
    public void Clear()
    {
        _programmedKeys.Clear();
    }

    /// <summary>
    /// Gets all programmed keys
    /// </summary>
    public Dictionary<int, string> GetAllProgrammedKeys()
    {
        return new Dictionary<int, string>(_programmedKeys);
    }

    /// <summary>
    /// Gets the number of programmed keys
    /// </summary>
    public int ProgrammedKeyCount => _programmedKeys.Count;
}
