using System;
using RetroTerm.Core.Terminal.Buffer;

namespace RetroTerm.Core.Terminal.Emulators.TDV;

/// <summary>
/// Manages protected areas (SPA/EPA) for TDV terminals
/// Protected areas prevent cursor movement and text modification
/// </summary>
public class TDVProtectedAreas
{
    private readonly bool[,] _protectedCells;
    private readonly int _width;
    private readonly int _height;

    public TDVProtectedAreas(int width, int height)
    {
        _width = width;
        _height = height;
        _protectedCells = new bool[height, width];
    }

    /// <summary>
    /// Sets a protected area (SPA - Start Protected Area)
    /// </summary>
    public void SetProtectedArea(int row, int col)
    {
        if (IsValidPosition(row, col))
        {
            _protectedCells[row, col] = true;
        }
    }

    /// <summary>
    /// Clears a protected area (EPA - End Protected Area)
    /// </summary>
    public void ClearProtectedArea(int row, int col)
    {
        if (IsValidPosition(row, col))
        {
            _protectedCells[row, col] = false;
        }
    }

    /// <summary>
    /// Sets a rectangular protected area
    /// </summary>
    public void SetProtectedRectangle(int top, int left, int bottom, int right)
    {
        for (int row = Math.Max(0, top); row <= Math.Min(_height - 1, bottom); row++)
        {
            for (int col = Math.Max(0, left); col <= Math.Min(_width - 1, right); col++)
            {
                _protectedCells[row, col] = true;
            }
        }
    }

    /// <summary>
    /// Clears a rectangular protected area
    /// </summary>
    public void ClearProtectedRectangle(int top, int left, int bottom, int right)
    {
        for (int row = Math.Max(0, top); row <= Math.Min(_height - 1, bottom); row++)
        {
            for (int col = Math.Max(0, left); col <= Math.Min(_width - 1, right); col++)
            {
                _protectedCells[row, col] = false;
            }
        }
    }

    /// <summary>
    /// Checks if a position is protected
    /// </summary>
    public bool IsProtected(int row, int col)
    {
        return IsValidPosition(row, col) && _protectedCells[row, col];
    }

    /// <summary>
    /// Clears all protected areas
    /// </summary>
    public void Clear()
    {
        Array.Clear(_protectedCells, 0, _protectedCells.Length);
    }

    /// <summary>
    /// Gets all protected positions
    /// </summary>
    public (int Row, int Col)[] GetProtectedPositions()
    {
        var positions = new List<(int, int)>();

        for (int row = 0; row < _height; row++)
        {
            for (int col = 0; col < _width; col++)
            {
                if (_protectedCells[row, col])
                {
                    positions.Add((row, col));
                }
            }
        }

        return positions.ToArray();
    }

    private bool IsValidPosition(int row, int col)
    {
        return row >= 0 && row < _height && col >= 0 && col < _width;
    }
}
