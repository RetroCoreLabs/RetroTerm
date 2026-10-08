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
    /// Every graphic rendition aspect a cell can carry from SGR. NDSAR resets all of these before
    /// it sets the ones it was given; the double-width, double-height and protected bits are not
    /// graphic rendition and are left alone.
    /// </summary>
    private const CharacterAttributes AllRenditionAspects =
        CharacterAttributes.Bold | CharacterAttributes.Dim | CharacterAttributes.Italic |
        CharacterAttributes.Underline | CharacterAttributes.Blink | CharacterAttributes.RapidBlink |
        CharacterAttributes.Reverse | CharacterAttributes.Hidden | CharacterAttributes.Strikethrough;

    /// <summary>
    /// Maps one attribute number to the aspect it stands for, using the SGR table of ND Display
    /// Terminal 1200 section 5.67 - the table NDSAR, NDAAR and NDRAR point at ("see SGR").
    /// </summary>
    /// <remarks>
    /// 0 is "reset to default rendition" and 1, 3 and 6 are "Ignored" in that table, so they, and
    /// every number the table does not list, map to <see cref="CharacterAttributes.None"/>: they
    /// change nothing. 2 is low intensity, 4 underlined, 5 slow blink, 7 inverse, 8 invisible.
    /// </remarks>
    public static CharacterAttributes AspectForAttributeNumber(int attribute)
    {
        switch (attribute)
        {
            case 2: return CharacterAttributes.Dim;
            case 4: return CharacterAttributes.Underline;
            case 5: return CharacterAttributes.Blink;
            case 7: return CharacterAttributes.Reverse;
            case 8: return CharacterAttributes.Hidden;
            default: return CharacterAttributes.None;
        }
    }

    /// <summary>
    /// Folds a list of attribute numbers into one set of aspects.
    /// </summary>
    private static CharacterAttributes AspectsFor(ReadOnlySpan<int> attributes)
    {
        var aspects = CharacterAttributes.None;
        for (int i = 0; i < attributes.Length; i++)
        {
            aspects |= AspectForAttributeNumber(attributes[i]);
        }

        return aspects;
    }

    /// <summary>
    /// Sets the attributes in a rectangle (NDSAR). Whatever rendition the cells had is reset
    /// first: "previously specified aspects within the rectangle shall be reset" (section 5.50).
    /// An empty list leaves the cells in normal rendition, which is the function's default.
    /// </summary>
    public void SetAttributeInRectangle(TerminalBuffer buffer, ReadOnlySpan<int> attributes, int x1, int y1, int x2, int y2)
    {
        var (left, top, right, bottom) = NormalizeRectangle(x1, y1, x2, y2, buffer.Width, buffer.Height);
        var aspects = AspectsFor(attributes);

        for (int row = top; row <= bottom; row++)
        {
            for (int col = left; col <= right; col++)
            {
                ref var cell = ref buffer[row, col];
                cell.Attributes = (cell.Attributes & ~AllRenditionAspects) | aspects;
            }
        }
    }

    /// <summary>
    /// Adds attributes to a rectangle (NDAAR). Aspects already there "shall remain in effect"
    /// (section 5.36).
    /// </summary>
    public void AddAttributeInRectangle(TerminalBuffer buffer, ReadOnlySpan<int> attributes, int x1, int y1, int x2, int y2)
    {
        var (left, top, right, bottom) = NormalizeRectangle(x1, y1, x2, y2, buffer.Width, buffer.Height);
        var aspects = AspectsFor(attributes);

        for (int row = top; row <= bottom; row++)
        {
            for (int col = left; col <= right; col++)
            {
                ref var cell = ref buffer[row, col];
                cell.Attributes |= aspects;
            }
        }
    }

    /// <summary>
    /// Removes attributes from a rectangle (NDRAR). Only the aspects named are removed.
    /// </summary>
    public void RemoveAttributeInRectangle(TerminalBuffer buffer, ReadOnlySpan<int> attributes, int x1, int y1, int x2, int y2)
    {
        var (left, top, right, bottom) = NormalizeRectangle(x1, y1, x2, y2, buffer.Width, buffer.Height);
        var aspects = AspectsFor(attributes);

        for (int row = top; row <= bottom; row++)
        {
            for (int col = left; col <= right; col++)
            {
                ref var cell = ref buffer[row, col];
                cell.Attributes &= ~aspects;
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
}
