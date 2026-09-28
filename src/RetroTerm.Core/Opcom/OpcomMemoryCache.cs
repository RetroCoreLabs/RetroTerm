using System;
using System.Collections.Generic;

namespace RetroTerm.Core.Opcom;

/// <summary>
/// Caches memory values read from the ND-100 via OPCOM.
/// Supports both individual reads and bulk dumps.
/// </summary>
public sealed class OpcomMemoryCache
{
    private readonly Dictionary<int, ushort> _cache = new();
    private readonly object _lock = new();

    /// <summary>
    /// Gets the number of cached entries.
    /// </summary>
    public int Count
    {
        get { lock (_lock) return _cache.Count; }
    }

    /// <summary>
    /// Stores a value at the given address.
    /// </summary>
    public void Set(int address, ushort value)
    {
        lock (_lock)
        {
            _cache[address] = value;
        }
        MemoryChanged?.Invoke(address, value);
    }

    /// <summary>
    /// Stores multiple values starting at the given address.
    /// </summary>
    public void SetRange(int startAddress, ushort[] values, int count)
    {
        lock (_lock)
        {
            for (int i = 0; i < count; i++)
            {
                _cache[startAddress + i] = values[i];
            }
        }
        MemoryRangeChanged?.Invoke(startAddress, count);
    }

    /// <summary>
    /// Tries to get a cached value. Returns false if the address hasn't been read.
    /// </summary>
    public bool TryGet(int address, out ushort value)
    {
        lock (_lock)
        {
            return _cache.TryGetValue(address, out value);
        }
    }

    /// <summary>
    /// Gets a range of cached values. Fills destination with values and returns a
    /// boolean array indicating which addresses were cached.
    /// </summary>
    public void GetRange(int startAddress, int count, ushort[] values, bool[] valid)
    {
        lock (_lock)
        {
            for (int i = 0; i < count; i++)
            {
                if (_cache.TryGetValue(startAddress + i, out ushort val))
                {
                    values[i] = val;
                    valid[i] = true;
                }
                else
                {
                    values[i] = 0;
                    valid[i] = false;
                }
            }
        }
    }

    /// <summary>
    /// Clears all cached values.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
        CacheCleared?.Invoke();
    }

    /// <summary>
    /// Event raised when a single memory value changes.
    /// </summary>
    public event Action<int, ushort>? MemoryChanged;

    /// <summary>
    /// Event raised when a range of memory values changes.
    /// </summary>
    public event Action<int, int>? MemoryRangeChanged;

    /// <summary>
    /// Event raised when the cache is cleared.
    /// </summary>
    public event Action? CacheCleared;
}
