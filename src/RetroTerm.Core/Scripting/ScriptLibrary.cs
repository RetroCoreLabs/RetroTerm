using System;
using System.Collections.Generic;
using System.IO;

namespace RetroTerm.Core.Scripting;

/// <summary>
/// The on-disk script library: plain-text .rts files in one folder, addressed by name
/// (the file name without extension). Default location is %AppData%\RetroTerm\scripts\,
/// the same storage pattern as tdv-key-bindings.json. Constructor takes the directory
/// so tests use a temp folder.
///
/// Deliberately dumb: no metadata files, no index — the folder IS the library, and a
/// user can drop a script in with a text editor and it just appears.
/// </summary>
public sealed class ScriptLibrary
{
    /// <summary>
    /// Script file extension, including the dot.
    /// </summary>
    public const string Extension = ".rts";

    private readonly string _directory;

    /// <summary>
    /// The directory this library reads and writes.
    /// </summary>
    public string Directory => _directory;

    /// <summary>
    /// Creates a library over the given directory (created on first save).
    /// </summary>
    public ScriptLibrary(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory must be set", nameof(directory));
        _directory = directory;
    }

    /// <summary>
    /// The default library under %AppData%\RetroTerm\scripts\.
    /// </summary>
    public static ScriptLibrary CreateDefault()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new ScriptLibrary(Path.Combine(appData, "RetroTerm", "scripts"));
    }

    /// <summary>
    /// Script names (file names without extension), sorted, empty when the folder does not exist.
    /// </summary>
    public IReadOnlyList<string> List()
    {
        var names = new List<string>();
        if (!System.IO.Directory.Exists(_directory))
        {
            return names;
        }

        var files = System.IO.Directory.GetFiles(_directory, "*" + Extension);
        for (int i = 0; i < files.Length; i++)
        {
            names.Add(Path.GetFileNameWithoutExtension(files[i]));
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    /// <summary>
    /// True when a script with this name exists.
    /// </summary>
    public bool Exists(string name) => File.Exists(PathFor(name));

    /// <summary>
    /// Loads a script's text. Throws FileNotFoundException with the resolved path when missing.
    /// </summary>
    public string Load(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Script '{name}' not found at {path}", path);
        }
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Saves (creates or overwrites) a script. Creates the library folder on first use.
    /// </summary>
    public void Save(string name, string scriptText)
    {
        if (scriptText == null) throw new ArgumentNullException(nameof(scriptText));
        System.IO.Directory.CreateDirectory(_directory);
        File.WriteAllText(PathFor(name), scriptText);
    }

    /// <summary>
    /// Deletes a script. No error when it does not exist.
    /// </summary>
    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Full path for a script name. Rejects names with path separators or traversal —
    /// the library is one flat folder, and MCP-supplied names must not escape it.
    /// </summary>
    public string PathFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Script name must be set", nameof(name));
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.IndexOf(Path.DirectorySeparatorChar) >= 0
            || name.IndexOf(Path.AltDirectorySeparatorChar) >= 0
            || name.Contains(".."))
        {
            throw new ArgumentException($"'{name}' is not a valid script name — plain file names only", nameof(name));
        }
        return Path.Combine(_directory, name + Extension);
    }
}
