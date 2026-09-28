using System;
using RetroTerm.Core.Terminal.Buffer;

namespace RetroTerm.Core.Terminal.Emulators.TDV;

/// <summary>
/// Handles rectangle operations for TDV terminals
/// Supports NDILWA, NDDLWA, NDICHE, NDDCHE operations
/// </summary>
public class TDVRectangleOperations
{
    private readonly Dictionary<string, TerminalCell[,]> _savedRectangles;

    public TDVRectangleOperations()
    {
        _savedRectangles = new Dictionary<string, TerminalCell[,]>();
    }

    /// <summary>
    /// Sets attribute in rectangle (NDSAR)
    /// </summary>
    public void SetAttributeInRectangle(TerminalBuffer buffer, int attr, int x1, int y1, int x2, int y2)
    {
        var (left, top, right, bottom) = NormalizeRectangle(x1, y1, x2, y2, buffer.Width, buffer.Height);

        for (int row = top; row <= bottom; row++)
        {
            for (int col = left; col <= right; col++)
            {
                ref var cell = ref buffer[row, col];
                ApplyAttribute(ref cell, attr);
            }
        }
    }

    /// <summary>
    /// Adds attribute in rectangle (NDAAR)
    /// </summary>
    public void AddAttributeInRectangle(TerminalBuffer buffer, int attr, int x1, int y1, int x2, int y2)
    {
        var (left, top, right, bottom) = NormalizeRectangle(x1, y1, x2, y2, buffer.Width, buffer.Height);

        for (int row = top; row <= bottom; row++)
        {
            for (int col = left; col <= right; col++)
            {
                ref var cell = ref buffer[row, col];
                AddAttribute(ref cell, attr);
            }
        }
    }

    /// <summary>
    /// Removes attribute in rectangle (NDRAR)
    /// </summary>
    public void RemoveAttributeInRectangle(TerminalBuffer buffer, int attr, int x1, int y1, int x2, int y2)
    {
        var (left, top, right, bottom) = NormalizeRectangle(x1, y1, x2, y2, buffer.Width, buffer.Height);

        for (int row = top; row <= bottom; row++)
        {
            for (int col = left; col <= right; col++)
            {
                ref var cell = ref buffer[row, col];
                RemoveAttribute(ref cell, attr);
            }
        }
    }

    /// <summary>
    /// Saves rectangle (NDSREC)
    /// </summary>
    public void SaveRectangle(TerminalBuffer buffer, int x1, int y1, int x2, int y2)
    {
        var (left, top, right, bottom) = NormalizeRectangle(x1, y1, x2, y2, buffer.Width, buffer.Height);
        var width = right - left + 1;
        var height = bottom - top + 1;

        var rectangle = new TerminalCell[height, width];

        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                rectangle[row, col] = buffer[top + row, left + col];
            }
        }

        var key = $"{left},{top},{right},{bottom}";
        _savedRectangles[key] = rectangle;
    }

    /// <summary>
    /// Restores rectangle (NDRREC)
    /// </summary>
    public void RestoreRectangle(TerminalBuffer buffer, int x, int y)
    {
        // Find the most recently saved rectangle
        var mostRecentKey = _savedRectangles.Keys.LastOrDefault();
        if (mostRecentKey == null) return;

        var rectangle = _savedRectangles[mostRecentKey];
        var height = rectangle.GetLength(0);
        var width = rectangle.GetLength(1);

        // Clamp to buffer bounds
        var startX = Math.Max(0, Math.Min(x, buffer.Width - width));
        var startY = Math.Max(0, Math.Min(y, buffer.Height - height));

        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                if (startY + row < buffer.Height && startX + col < buffer.Width)
                {
                    buffer[startY + row, startX + col] = rectangle[row, col];
                }
            }
        }
    }

    /// <summary>
    /// Fills character in rectangle (NDFC)
    /// </summary>
    public void FillCharacterInRectangle(TerminalBuffer buffer, int character, int x1, int y1, int x2, int y2)
    {
        var (left, top, right, bottom) = NormalizeRectangle(x1, y1, x2, y2, buffer.Width, buffer.Height);

        for (int row = top; row <= bottom; row++)
        {
            for (int col = left; col <= right; col++)
            {
                ref var cell = ref buffer[row, col];
                cell.Codepoint = (uint)character;
            }
        }
    }

    /// <summary>
    /// Clears all saved rectangles
    /// </summary>
    public void Clear()
    {
        _savedRectangles.Clear();
    }

    /// <summary>
    /// Normalizes rectangle coordinates
    /// </summary>
    private (int Left, int Top, int Right, int Bottom) NormalizeRectangle(int x1, int y1, int x2, int y2, int maxWidth, int maxHeight)
    {
        var left = Math.Max(0, Math.Min(x1, x2));
        var top = Math.Max(0, Math.Min(y1, y2));
        var right = Math.Min(maxWidth - 1, Math.Max(x1, x2));
        var bottom = Math.Min(maxHeight - 1, Math.Max(y1, y2));

        return (left, top, right, bottom);
    }

    /// <summary>
    /// Applies attribute to a cell
    /// </summary>
    private void ApplyAttribute(ref TerminalCell cell, int attr)
    {
        // Apply TDV-specific attributes based on the attribute value
        switch (attr)
        {
            case 1: // Bold
                cell.Attributes |= CharacterAttributes.Bold;
                break;
            case 2: // Dim
                cell.Attributes |= CharacterAttributes.Dim;
                break;
            case 4: // Underline
                cell.Attributes |= CharacterAttributes.Underline;
                break;
            case 5: // Blink
                cell.Attributes |= CharacterAttributes.Blink;
                break;
            case 7: // Reverse
                cell.Attributes |= CharacterAttributes.Reverse;
                break;
            case 8: // Hidden
                cell.Attributes |= CharacterAttributes.Hidden;
                break;
        }
    }

    /// <summary>
    /// Adds attribute to a cell
    /// </summary>
    private void AddAttribute(ref TerminalCell cell, int attr)
    {
        // Add TDV-specific attributes
        ApplyAttribute(ref cell, attr);
    }

    /// <summary>
    /// Removes attribute from a cell
    /// </summary>
    private void RemoveAttribute(ref TerminalCell cell, int attr)
    {
        // Remove TDV-specific attributes
        switch (attr)
        {
            case 1: // Bold
                cell.Attributes &= ~CharacterAttributes.Bold;
                break;
            case 2: // Dim
                cell.Attributes &= ~CharacterAttributes.Dim;
                break;
            case 4: // Underline
                cell.Attributes &= ~CharacterAttributes.Underline;
                break;
            case 5: // Blink
                cell.Attributes &= ~CharacterAttributes.Blink;
                break;
            case 7: // Reverse
                cell.Attributes &= ~CharacterAttributes.Reverse;
                break;
            case 8: // Hidden
                cell.Attributes &= ~CharacterAttributes.Hidden;
                break;
        }
    }
}
