using System.Linq;
using RetroTerm.Core.Search;
using RetroTerm.Core.Terminal.Buffer;
using Xunit;

namespace RetroTerm.Tests.Search;

/// <summary>
/// Unit tests for ScrollbackSearch
/// </summary>
public class ScrollbackSearchTests
{
    [Fact]
    public void Search_PlainText_ShouldFindMatches()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        buffer[0, 0] = new TerminalCell('H');
        buffer[0, 1] = new TerminalCell('e');
        buffer[0, 2] = new TerminalCell('l');
        buffer[0, 3] = new TerminalCell('l');
        buffer[0, 4] = new TerminalCell('o');

        var search = new ScrollbackSearch(buffer);
        var options = new SearchOptions { CaseSensitive = true };

        // Act
        var matches = search.Search("Hello", options);

        // Assert
        Assert.Single(matches);
        Assert.Equal(0, matches[0].Row);
        Assert.Equal(0, matches[0].StartCol);
        Assert.Equal(4, matches[0].EndCol);
    }

    [Fact]
    public void Search_CaseInsensitive_ShouldFindMatches()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        buffer[0, 0] = new TerminalCell('H');
        buffer[0, 1] = new TerminalCell('e');
        buffer[0, 2] = new TerminalCell('l');
        buffer[0, 3] = new TerminalCell('l');
        buffer[0, 4] = new TerminalCell('o');

        var search = new ScrollbackSearch(buffer);
        var options = new SearchOptions { CaseSensitive = false };

        // Act
        var matches = search.Search("hello", options);

        // Assert
        Assert.Single(matches);
    }

    [Fact]
    public void Search_MultipleMatches_ShouldFindAll()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        buffer[0, 0] = new TerminalCell('T');
        buffer[0, 1] = new TerminalCell('e');
        buffer[0, 2] = new TerminalCell('s');
        buffer[0, 3] = new TerminalCell('t');
        buffer[0, 4] = new TerminalCell(' ');
        buffer[0, 5] = new TerminalCell('T');
        buffer[0, 6] = new TerminalCell('e');
        buffer[0, 7] = new TerminalCell('s');
        buffer[0, 8] = new TerminalCell('t');

        var search = new ScrollbackSearch(buffer);
        var options = new SearchOptions { CaseSensitive = true };

        // Act
        var matches = search.Search("Test", options);

        // Assert
        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void Search_WholeWord_ShouldMatchOnlyWholeWords()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        buffer[0, 0] = new TerminalCell('T');
        buffer[0, 1] = new TerminalCell('e');
        buffer[0, 2] = new TerminalCell('s');
        buffer[0, 3] = new TerminalCell('t');
        buffer[0, 4] = new TerminalCell('i');
        buffer[0, 5] = new TerminalCell('n');
        buffer[0, 6] = new TerminalCell('g');

        var search = new ScrollbackSearch(buffer);
        var options = new SearchOptions { CaseSensitive = true, WholeWord = true };

        // Act
        var matches = search.Search("Test", options);

        // Assert
        Assert.Empty(matches); // "Test" is part of "Testing", not a whole word
    }

    [Fact]
    public void Search_Regex_ShouldUseRegexPattern()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        buffer[0, 0] = new TerminalCell('1');
        buffer[0, 1] = new TerminalCell('2');
        buffer[0, 2] = new TerminalCell('3');
        buffer[0, 3] = new TerminalCell(' ');
        buffer[0, 4] = new TerminalCell('4');
        buffer[0, 5] = new TerminalCell('5');
        buffer[0, 6] = new TerminalCell('6');

        var search = new ScrollbackSearch(buffer);
        var options = new SearchOptions { UseRegex = true };

        // Act
        var matches = search.Search(@"\d+", options);

        // Assert
        Assert.Equal(2, matches.Count); // Should find "123" and "456"
    }

    [Fact]
    public void FindNext_ShouldReturnNextMatch()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        buffer[0, 0] = new TerminalCell('T');
        buffer[0, 1] = new TerminalCell('e');
        buffer[0, 2] = new TerminalCell('s');
        buffer[0, 3] = new TerminalCell('t');
        buffer[1, 0] = new TerminalCell('T');
        buffer[1, 1] = new TerminalCell('e');
        buffer[1, 2] = new TerminalCell('s');
        buffer[1, 3] = new TerminalCell('t');

        var search = new ScrollbackSearch(buffer);
        var options = new SearchOptions { CaseSensitive = true };
        var matches = search.Search("Test", options);

        // Act
        var next = search.FindNext(matches, 0, 0);

        // Assert
        Assert.NotNull(next);
        Assert.Equal(1, next.Row);
    }

    [Fact]
    public void FindPrevious_ShouldReturnPreviousMatch()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        buffer[0, 0] = new TerminalCell('T');
        buffer[0, 1] = new TerminalCell('e');
        buffer[0, 2] = new TerminalCell('s');
        buffer[0, 3] = new TerminalCell('t');
        buffer[1, 0] = new TerminalCell('T');
        buffer[1, 1] = new TerminalCell('e');
        buffer[1, 2] = new TerminalCell('s');
        buffer[1, 3] = new TerminalCell('t');

        var search = new ScrollbackSearch(buffer);
        var options = new SearchOptions { CaseSensitive = true };
        var matches = search.Search("Test", options);

        // Act
        var prev = search.FindPrevious(matches, 1, 0);

        // Assert
        Assert.NotNull(prev);
        Assert.Equal(0, prev.Row);
    }

    [Fact]
    public void Search_EmptyPattern_ShouldReturnEmpty()
    {
        // Arrange
        var buffer = new TerminalBuffer(80, 24);
        var search = new ScrollbackSearch(buffer);
        var options = new SearchOptions();

        // Act
        var matches = search.Search("", options);

        // Assert
        Assert.Empty(matches);
    }
}

