using System;
using System.IO;
using RetroTerm.Core.Scripting;
using Xunit;

namespace RetroTerm.Tests.Scripting;

/// <summary>
/// Tests for the on-disk script library (P3.3) using a per-test temp folder.
/// </summary>
public class ScriptLibraryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScriptLibrary _library;

    public ScriptLibraryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RetroTermScriptLibTests-" + Guid.NewGuid().ToString("N"));
        _library = new ScriptLibrary(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void List_EmptyWhenFolderDoesNotExist()
    {
        Assert.Empty(_library.List());
    }

    [Fact]
    public void SaveLoadListDelete_RoundTrip()
    {
        _library.Save("login", "SENDRAW ESC\nWAITFOR \"ENTER\"\n");
        _library.Save("restart", "SEND \"RESTART\"\n");

        Assert.True(_library.Exists("login"));
        Assert.Equal(new[] { "login", "restart" }, _library.List());
        Assert.Contains("SENDRAW ESC", _library.Load("login"));

        _library.Delete("login");
        Assert.False(_library.Exists("login"));
        Assert.Equal(new[] { "restart" }, _library.List());
    }

    [Fact]
    public void Save_OverwritesExisting()
    {
        _library.Save("s", "old");
        _library.Save("s", "new");
        Assert.Equal("new", _library.Load("s"));
    }

    [Fact]
    public void Load_MissingScript_ThrowsWithPath()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => _library.Load("nope"));
        Assert.Contains("nope", ex.Message);
        Assert.Contains(_tempDir, ex.Message);
    }

    [Fact]
    public void PathFor_RejectsTraversalAndSeparators()
    {
        Assert.Throws<ArgumentException>(() => _library.PathFor("..\\evil"));
        Assert.Throws<ArgumentException>(() => _library.PathFor("a/b"));
        Assert.Throws<ArgumentException>(() => _library.PathFor("a\\b"));
        Assert.Throws<ArgumentException>(() => _library.PathFor(""));
    }

    [Fact]
    public void DroppedInFile_AppearsInList()
    {
        // The folder IS the library: a hand-made file counts.
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "handmade.rts"), "STATUS\n");

        Assert.Equal(new[] { "handmade" }, _library.List());
        Assert.Contains("STATUS", _library.Load("handmade"));
    }
}
