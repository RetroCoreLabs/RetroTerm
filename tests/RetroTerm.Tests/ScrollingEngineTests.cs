using System;
using RetroTerm.Core.Terminal.Rendering;
using Xunit;

namespace RetroTerm.Tests;

public class ScrollingEngineTests
{
    [Fact]
    public void ScrollingEngine_DefaultMode_ShouldBeStep()
    {
        // Arrange & Act
        var engine = new ScrollingEngine();

        // Assert
        Assert.Equal(ScrollMode.Step, engine.Mode);
    }

    [Fact]
    public void ScrollingEngine_StepMode_ShouldNotAnimate()
    {
        // Arrange
        var engine = new ScrollingEngine { Mode = ScrollMode.Step };

        // Act
        engine.Scroll(5);

        // Assert
        Assert.False(engine.IsScrolling);
        Assert.Equal(0.0, engine.SmoothScrollOffset);
    }

    [Fact]
    public void ScrollingEngine_SmoothMode_ShouldInitiateAnimation()
    {
        // Arrange
        var engine = new ScrollingEngine
        {
            Mode = ScrollMode.Smooth,
            LineHeight = 16
        };

        // Act
        engine.Scroll(5); // Scroll 5 lines

        // Assert
        Assert.True(engine.IsScrolling);
        Assert.Equal(0.0, engine.SmoothScrollOffset); // Animation hasn't started yet
    }

    [Fact]
    public void ScrollingEngine_UpdateSmoothScroll_ShouldAnimateTowardsTarget()
    {
        // Arrange
        var engine = new ScrollingEngine
        {
            Mode = ScrollMode.Smooth,
            LineHeight = 16,
            SmoothScrollSpeed = 160.0 // 160 pixels per second
        };
        engine.Scroll(5); // Target: 80 pixels (5 * 16)

        // Act
        double deltaTime = 1.0 / 60.0; // 60 FPS (16.67ms)
        bool stillScrolling = engine.UpdateSmoothScroll(deltaTime);

        // Assert
        Assert.True(stillScrolling);
        Assert.True(engine.SmoothScrollOffset > 0.0);
        Assert.True(engine.SmoothScrollOffset < 80.0);
    }

    [Fact]
    public void ScrollingEngine_UpdateSmoothScroll_ShouldCompleteAnimation()
    {
        // Arrange
        var engine = new ScrollingEngine
        {
            Mode = ScrollMode.Smooth,
            LineHeight = 16,
            SmoothScrollSpeed = 160.0
        };
        engine.Scroll(1); // Target: 16 pixels

        // Act: Update for enough time to complete the animation
        bool stillScrolling = true;
        int iterations = 0;
        while (stillScrolling && iterations < 100)
        {
            stillScrolling = engine.UpdateSmoothScroll(1.0 / 60.0);
            iterations++;
        }

        // Assert
        Assert.False(engine.IsScrolling);
        Assert.Equal(16.0, engine.SmoothScrollOffset, 10); // Allow floating-point precision tolerance
    }

    [Fact]
    public void ScrollingEngine_CompleteScrollAnimation_ShouldSnapToTarget()
    {
        // Arrange
        var engine = new ScrollingEngine
        {
            Mode = ScrollMode.Smooth,
            LineHeight = 16
        };
        engine.Scroll(5); // Target: 80 pixels
        engine.UpdateSmoothScroll(1.0 / 60.0); // Start animation

        // Act
        engine.CompleteScrollAnimation();

        // Assert
        Assert.False(engine.IsScrolling);
        Assert.Equal(80.0, engine.SmoothScrollOffset);
    }

    [Fact]
    public void ScrollingEngine_ResetScrollState_ShouldClearAnimation()
    {
        // Arrange
        var engine = new ScrollingEngine
        {
            Mode = ScrollMode.Smooth,
            LineHeight = 16
        };
        engine.Scroll(5);
        engine.UpdateSmoothScroll(1.0 / 60.0);

        // Act
        engine.ResetScrollState();

        // Assert
        Assert.False(engine.IsScrolling);
        Assert.Equal(0.0, engine.SmoothScrollOffset);
    }

    [Fact]
    public void ScrollingEngine_GetCompletedScrollLines_ShouldReturnCorrectCount()
    {
        // Arrange
        var engine = new ScrollingEngine
        {
            Mode = ScrollMode.Smooth,
            LineHeight = 16
        };
        engine.Scroll(5); // Target: 80 pixels

        // Act: Animate to 40 pixels (2.5 lines)
        while (engine.SmoothScrollOffset < 40.0)
        {
            engine.UpdateSmoothScroll(1.0 / 60.0);
        }

        // Assert
        int completedLines = engine.GetCompletedScrollLines();
        Assert.True(completedLines >= 2 && completedLines <= 3); // Should be 2 or 3 depending on rounding
    }

    [Fact]
    public void ScrollingEngine_AcknowledgeBufferScroll_ShouldAdjustOffsets()
    {
        // Arrange
        var engine = new ScrollingEngine
        {
            Mode = ScrollMode.Smooth,
            LineHeight = 16
        };
        engine.Scroll(5); // Target: 80 pixels

        // Act: Animate to 48 pixels (3 lines)
        while (engine.SmoothScrollOffset < 48.0)
        {
            engine.UpdateSmoothScroll(1.0 / 60.0);
        }
        engine.AcknowledgeBufferScroll(3); // Buffer scrolled 3 lines

        // Assert
        Assert.True(engine.SmoothScrollOffset < 16.0); // Offset reduced by 48 pixels
        Assert.True(engine.IsScrolling); // Still scrolling to reach target
    }

    [Fact]
    public void ScrollingEngine_SmoothScrollSpeed_ShouldAffectAnimationSpeed()
    {
        // Arrange
        var fastEngine = new ScrollingEngine
        {
            Mode = ScrollMode.Smooth,
            LineHeight = 16,
            SmoothScrollSpeed = 320.0 // Fast
        };
        var slowEngine = new ScrollingEngine
        {
            Mode = ScrollMode.Smooth,
            LineHeight = 16,
            SmoothScrollSpeed = 80.0 // Slow
        };

        fastEngine.Scroll(5);
        slowEngine.Scroll(5);

        // Act
        double deltaTime = 1.0 / 60.0;
        fastEngine.UpdateSmoothScroll(deltaTime);
        slowEngine.UpdateSmoothScroll(deltaTime);

        // Assert
        Assert.True(fastEngine.SmoothScrollOffset > slowEngine.SmoothScrollOffset);
    }

    [Fact]
    public void ScrollingEngine_NegativeScroll_ShouldScrollDown()
    {
        // Arrange
        var engine = new ScrollingEngine
        {
            Mode = ScrollMode.Smooth,
            LineHeight = 16
        };

        // Act
        engine.Scroll(-3); // Scroll down 3 lines

        // Assert
        Assert.True(engine.IsScrolling);
        // Target should be negative (scrolling down)
        engine.CompleteScrollAnimation();
        Assert.Equal(-48.0, engine.SmoothScrollOffset);
    }
}

