using System;

namespace RetroTerm.Core.Terminal.Rendering;

/// <summary>
/// Scroll mode for the terminal
/// </summary>
public enum ScrollMode
{
    /// <summary>
    /// Instant scrolling (STEP mode) - immediate buffer update
    /// </summary>
    Step,

    /// <summary>
    /// Smooth animated scrolling (SMOOTH mode) - interpolated visual scrolling
    /// </summary>
    Smooth
}

/// <summary>
/// Manages terminal scrolling behavior and smooth scroll animation state
/// </summary>
public class ScrollingEngine
{
    private ScrollMode _scrollMode = ScrollMode.Step;
    private double _smoothScrollOffset = 0.0;
    private double _smoothScrollSpeed = 120.0; // pixels per second
    private double _targetScrollOffset = 0.0;
    private int _lineHeight = 16; // default line height in pixels

    /// <summary>
    /// Gets or sets the current scroll mode
    /// </summary>
    public ScrollMode Mode
    {
        get => _scrollMode;
        set => _scrollMode = value;
    }

    /// <summary>
    /// Gets or sets the smooth scroll animation speed in pixels per second
    /// Default is 120 pixels/second (targeting ~60fps for smooth animation)
    /// </summary>
    public double SmoothScrollSpeed
    {
        get => _smoothScrollSpeed;
        set => _smoothScrollSpeed = Math.Max(1.0, value);
    }

    /// <summary>
    /// Gets the current smooth scroll pixel offset
    /// This value is used by the renderer to offset the display during smooth scrolling
    /// </summary>
    public double SmoothScrollOffset => _smoothScrollOffset;

    /// <summary>
    /// Gets whether a smooth scroll animation is currently in progress
    /// </summary>
    public bool IsScrolling => Math.Abs(_smoothScrollOffset - _targetScrollOffset) > 0.1;

    /// <summary>
    /// Gets or sets the line height in pixels (used for smooth scrolling calculations)
    /// </summary>
    public int LineHeight
    {
        get => _lineHeight;
        set => _lineHeight = Math.Max(1, value);
    }

    /// <summary>
    /// Initiates a scroll operation
    /// </summary>
    /// <param name="lines">
    /// Number of lines to scroll (positive = scroll up, negative = scroll down)
    /// </param>
    public void Scroll(int lines)
    {
        if (_scrollMode == ScrollMode.Step)
        {
            // Instant scroll - no animation
            _smoothScrollOffset = 0.0;
            _targetScrollOffset = 0.0;
        }
        else
        {
            // Smooth scroll - set target offset for animation
            _targetScrollOffset += lines * _lineHeight;
        }
    }

    /// <summary>
    /// Updates the smooth scroll animation state
    /// Should be called every frame when smooth scrolling is active
    /// </summary>
    /// <param name="deltaTime">
    /// Time elapsed since last update in seconds
    /// </param>
    /// <returns>
    /// True if the animation is still in progress, false if complete
    /// </returns>
    public bool UpdateSmoothScroll(double deltaTime)
    {
        if (_scrollMode != ScrollMode.Smooth || !IsScrolling)
        {
            return false;
        }

        // Calculate the distance to move this frame
        double distance = _smoothScrollSpeed * deltaTime;
        double remaining = _targetScrollOffset - _smoothScrollOffset;

        if (Math.Abs(remaining) <= distance)
        {
            // Snap to target if we're close enough
            _smoothScrollOffset = _targetScrollOffset;
            return false;
        }
        else
        {
            // Move towards target
            _smoothScrollOffset += Math.Sign(remaining) * distance;
            return true;
        }
    }

    /// <summary>
    /// Resets the scroll animation state
    /// Called when the buffer is updated or scrolling completes
    /// </summary>
    public void ResetScrollState()
    {
        _smoothScrollOffset = 0.0;
        _targetScrollOffset = 0.0;
    }

    /// <summary>
    /// Immediately completes any in-progress smooth scroll animation
    /// </summary>
    public void CompleteScrollAnimation()
    {
        _smoothScrollOffset = _targetScrollOffset;
    }

    /// <summary>
    /// Gets the number of complete lines that have been scrolled
    /// Used to determine when to update the buffer during smooth scrolling
    /// </summary>
    public int GetCompletedScrollLines()
    {
        if (_lineHeight == 0)
            return 0;

        return (int)(_smoothScrollOffset / _lineHeight);
    }

    /// <summary>
    /// Acknowledges that the buffer has been updated for the specified number of lines
    /// Adjusts the scroll offset accordingly
    /// </summary>
    /// <param name="lines">
    /// Number of lines that were scrolled in the buffer
    /// </param>
    public void AcknowledgeBufferScroll(int lines)
    {
        double scrolledPixels = lines * _lineHeight;
        _smoothScrollOffset -= scrolledPixels;
        _targetScrollOffset -= scrolledPixels;
    }
}

