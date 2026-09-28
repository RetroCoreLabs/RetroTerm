using System;

namespace RetroTerm.Core.Terminal.Emulators.TDV;

/// <summary>
/// Manages work areas (NDDWA) for TDV terminals
/// Work areas define rectangular regions for operations
/// </summary>
public class TDVWorkAreas
{
    private readonly int _width;
    private readonly int _height;
    private int _currentLeft;
    private int _currentTop;
    private int _currentRight;
    private int _currentBottom;
    private bool _hasWorkArea;

    public TDVWorkAreas(int width, int height)
    {
        _width = width;
        _height = height;
        _hasWorkArea = false;
    }

    /// <summary>
    /// Defines a work area (NDDWA - Define Work Area)
    /// </summary>
    public void DefineWorkArea(int x1, int y1, int x2, int y2)
    {
        // Normalize coordinates (ensure x1 <= x2, y1 <= y2)
        _currentLeft = Math.Min(x1, x2);
        _currentTop = Math.Min(y1, y2);
        _currentRight = Math.Max(x1, x2);
        _currentBottom = Math.Max(y1, y2);

        // Clamp to terminal bounds
        _currentLeft = Math.Max(0, Math.Min(_currentLeft, _width - 1));
        _currentTop = Math.Max(0, Math.Min(_currentTop, _height - 1));
        _currentRight = Math.Max(0, Math.Min(_currentRight, _width - 1));
        _currentBottom = Math.Max(0, Math.Min(_currentBottom, _height - 1));

        _hasWorkArea = true;
    }

    /// <summary>
    /// Gets the current work area bounds
    /// </summary>
    public (int Left, int Top, int Right, int Bottom) GetCurrentWorkArea()
    {
        if (!_hasWorkArea)
        {
            // Default to full screen
            return (0, 0, _width - 1, _height - 1);
        }

        return (_currentLeft, _currentTop, _currentRight, _currentBottom);
    }

    /// <summary>
    /// Checks if a position is within the current work area
    /// </summary>
    public bool IsInWorkArea(int row, int col)
    {
        if (!_hasWorkArea)
        {
            return true; // Full screen if no work area defined
        }

        // Work area boundaries are exclusive (not including the boundary positions)
        return col > _currentLeft && col < _currentRight &&
               row > _currentTop && row < _currentBottom;
    }

    /// <summary>
    /// Clears the current work area (returns to full screen)
    /// </summary>
    public void Clear()
    {
        _hasWorkArea = false;
    }

    /// <summary>
    /// Gets the work area dimensions
    /// </summary>
    public (int Width, int Height) GetWorkAreaDimensions()
    {
        var (left, top, right, bottom) = GetCurrentWorkArea();
        return (right - left + 1, bottom - top + 1);
    }

    /// <summary>
    /// Checks if a work area is currently defined
    /// </summary>
    public bool HasWorkArea => _hasWorkArea;
}
