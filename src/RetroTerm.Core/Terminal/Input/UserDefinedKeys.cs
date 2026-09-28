using System;
using System.Collections.Generic;

namespace RetroTerm.Core.Terminal.Input;

/// <summary>
/// The strings a host has loaded into this terminal's function keys with DECUDK.
/// </summary>
/// <remarks>
/// <para><b>What DECUDK is for</b></para>
/// A host loads a key once and then a single keypress sends a whole command, without the host
/// having to be told about it again. That is how a VT220-era application put its own menu on F6 to
/// F20.
///
/// <para><b>The lock is a real rule, not a nicety</b></para>
/// The sequence carries a lock parameter, and the DEFAULT is to lock. Once locked, further DECUDK
/// is ignored until a hard reset. That is deliberate in the original design: an application that
/// has set up its keys does not want the next thing that scrolls past - a stray file, a message
/// from another user - redefining them underneath it. A terminal that ignored the lock would let
/// any text on the wire rewrite what a keypress sends.
///
/// <para><b>Memory is bounded</b></para>
/// A real VT220 had a small fixed store and refused what did not fit. The limits here are larger
/// than the hardware's but they exist for the same reason: nothing arriving from a host may make
/// this terminal allocate without end.
/// </remarks>
public sealed class UserDefinedKeys
{
    /// <summary>
    /// Longest string one key may hold, in bytes.
    /// </summary>
    public const int MaximumKeyLength = 256;

    /// <summary>
    /// Total bytes across all keys.
    /// </summary>
    /// <remarks>
    /// A real VT220 had roughly 256 bytes for the lot. This is more generous, because there is no
    /// hardware here to run out - but it is still a ceiling, so a host cannot grow this store
    /// forever.
    /// </remarks>
    public const int MaximumTotalLength = 4096;

    private readonly Dictionary<int, byte[]> _definitions = new Dictionary<int, byte[]>();
    private int _totalLength;

    /// <summary>
    /// Whether the keys are locked against further definition.
    /// </summary>
    public bool IsLocked { get; private set; }

    /// <summary>
    /// How many keys are defined.
    /// </summary>
    public int Count => _definitions.Count;

    /// <summary>
    /// Defines one key.
    /// </summary>
    /// <param name="keyCode">
    /// The DEC key code, e.g. 17 for F6.
    /// </param>
    /// <param name="value">
    /// The bytes that key should send. An empty value clears the key.
    /// </param>
    /// <returns>
    /// False when the keys are locked, or the definition does not fit.
    /// </returns>
    public bool Define(int keyCode, byte[] value)
    {
        if (IsLocked) return false;
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (value.Length > MaximumKeyLength) return false;

        // Replacing a key frees what it held, so a host redefining the same key repeatedly does not
        // creep towards the ceiling.
        if (_definitions.TryGetValue(keyCode, out var existing))
        {
            _totalLength -= existing.Length;
            _definitions.Remove(keyCode);
        }

        if (value.Length == 0) return true;

        if (_totalLength + value.Length > MaximumTotalLength)
        {
            // Put the old one back rather than leaving the key half-changed.
            if (existing != null)
            {
                _definitions[keyCode] = existing;
                _totalLength += existing.Length;
            }
            return false;
        }

        _definitions[keyCode] = value;
        _totalLength += value.Length;
        return true;
    }

    /// <summary>
    /// Reads the string loaded into a key.
    /// </summary>
    /// <param name="keyCode">
    /// The DEC key code.
    /// </param>
    /// <param name="value">
    /// The bytes that key sends, when it is defined.
    /// </param>
    /// <returns>
    /// True when the key has a definition.
    /// </returns>
    public bool TryGet(int keyCode, out byte[] value) => _definitions.TryGetValue(keyCode, out value!);

    /// <summary>
    /// Forgets every definition. Does not change the lock.
    /// </summary>
    public void Clear()
    {
        _definitions.Clear();
        _totalLength = 0;
    }

    /// <summary>
    /// Locks the keys against further definition.
    /// </summary>
    public void Lock() => IsLocked = true;

    /// <summary>
    /// Unlocks and forgets everything - what a hard reset does.
    /// </summary>
    /// <remarks>
    /// The ONLY way out of the lock, which is the point of it. If a host could unlock by asking,
    /// the lock would stop anything.
    /// </remarks>
    public void Reset()
    {
        Clear();
        IsLocked = false;
    }
}
