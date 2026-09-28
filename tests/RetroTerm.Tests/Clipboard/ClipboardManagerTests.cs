using System;
using RetroTerm.Core.Clipboard;
using Xunit;

namespace RetroTerm.Tests.Clipboard;

/// <summary>
/// Mock clipboard service for testing
/// </summary>
public class MockClipboardService : IClipboardService
{
    private string? _text;

    public string? GetText() => _text;

    public void SetText(string text) => _text = text;

    public bool ContainsText() => !string.IsNullOrEmpty(_text);
}

/// <summary>
/// Unit tests for ClipboardManager
/// </summary>
public class ClipboardManagerTests
{
    [Fact]
    public void Copy_ShouldSetTextInClipboard()
    {
        // Arrange
        var mockService = new MockClipboardService();
        var manager = new ClipboardManager(mockService);

        // Act
        manager.Copy("Hello World");

        // Assert
        Assert.Equal("Hello World", mockService.GetText());
    }

    [Fact]
    public void Copy_EmptyText_ShouldNotSetClipboard()
    {
        // Arrange
        var mockService = new MockClipboardService();
        var manager = new ClipboardManager(mockService);
        mockService.SetText("Existing");

        // Act
        manager.Copy("");

        // Assert
        Assert.Equal("Existing", mockService.GetText());
    }

    [Fact]
    public void Copy_ShouldRemoveControlCharacters()
    {
        // Arrange
        var mockService = new MockClipboardService();
        var manager = new ClipboardManager(mockService);
        var textWithControls = "Hello\x01World\x02Test"; // Contains control characters

        // Act
        manager.Copy(textWithControls);

        // Assert
        var result = mockService.GetText();
        Assert.NotNull(result);
        // Control characters should be removed
        Assert.Equal("HelloWorldTest", result);
        Assert.Contains("Hello", result);
        Assert.Contains("World", result);
        Assert.Contains("Test", result);
    }

    [Fact]
    public void Copy_ShouldPreserveNewlines()
    {
        // Arrange
        var mockService = new MockClipboardService();
        var manager = new ClipboardManager(mockService);
        var textWithNewlines = "Line1\nLine2\nLine3";

        // Act
        manager.Copy(textWithNewlines);

        // Assert
        var result = mockService.GetText();
        Assert.NotNull(result);
        Assert.Contains('\n', result);
        Assert.Contains("Line1", result);
        Assert.Contains("Line2", result);
        Assert.Contains("Line3", result);
    }

    [Fact]
    public void Paste_ShouldGetTextFromClipboard()
    {
        // Arrange
        var mockService = new MockClipboardService();
        mockService.SetText("Pasted Text");
        var manager = new ClipboardManager(mockService);

        // Act
        var result = manager.Paste();

        // Assert
        Assert.Equal("Pasted Text", result);
    }

    [Fact]
    public void Paste_EmptyClipboard_ShouldReturnNull()
    {
        // Arrange
        var mockService = new MockClipboardService();
        var manager = new ClipboardManager(mockService);

        // Act
        var result = manager.Paste();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Paste_ShouldConvertWindowsLineEndings()
    {
        // Arrange
        var mockService = new MockClipboardService();
        mockService.SetText("Line1\r\nLine2\r\nLine3");
        var manager = new ClipboardManager(mockService);

        // Act
        var result = manager.Paste();

        // Assert
        Assert.NotNull(result);
        Assert.DoesNotContain("\r", result);
        Assert.Contains("\n", result);
    }

    [Fact]
    public void Paste_ShouldRemoveDangerousControlCharacters()
    {
        // Arrange
        var mockService = new MockClipboardService();
        mockService.SetText("Hello\x1bWorld\x07Test"); // ESC and BEL
        var manager = new ClipboardManager(mockService);

        // Act
        var result = manager.Paste();

        // Assert
        Assert.NotNull(result);
        // Control characters should be removed, leaving only the text parts
        Assert.Equal("HelloWorldTest", result);
        Assert.Contains("Hello", result);
        Assert.Contains("World", result);
        Assert.Contains("Test", result);
    }

    [Fact]
    public void Paste_ShouldPreserveTabs()
    {
        // Arrange
        var mockService = new MockClipboardService();
        mockService.SetText("Column1\tColumn2\tColumn3");
        var manager = new ClipboardManager(mockService);

        // Act
        var result = manager.Paste();

        // Assert
        Assert.NotNull(result);
        Assert.Contains('\t', result);
    }

    [Fact]
    public void CanPaste_WithText_ShouldReturnTrue()
    {
        // Arrange
        var mockService = new MockClipboardService();
        mockService.SetText("Some text");
        var manager = new ClipboardManager(mockService);

        // Act
        var result = manager.CanPaste();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanPaste_WithoutText_ShouldReturnFalse()
    {
        // Arrange
        var mockService = new MockClipboardService();
        var manager = new ClipboardManager(mockService);

        // Act
        var result = manager.CanPaste();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void EstimateSize_ShouldReturnTextLength()
    {
        // Arrange
        var mockService = new MockClipboardService();
        var manager = new ClipboardManager(mockService);

        // Act
        var size = manager.EstimateSize("Hello World");

        // Assert
        Assert.Equal(11, size);
    }

    [Fact]
    public void IsLargePaste_AboveThreshold_ShouldReturnTrue()
    {
        // Arrange
        var mockService = new MockClipboardService();
        var manager = new ClipboardManager(mockService);
        var largeText = new string('A', 1500);

        // Act
        var isLarge = manager.IsLargePaste(largeText, threshold: 1000);

        // Assert
        Assert.True(isLarge);
    }

    [Fact]
    public void IsLargePaste_BelowThreshold_ShouldReturnFalse()
    {
        // Arrange
        var mockService = new MockClipboardService();
        var manager = new ClipboardManager(mockService);
        var smallText = new string('A', 500);

        // Act
        var isLarge = manager.IsLargePaste(smallText, threshold: 1000);

        // Assert
        Assert.False(isLarge);
    }
}

