using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RetroTerm.Tests.TDV.TDV2200Validation;

/// <summary>
/// Parses TDV-2200 specification documents and extracts feature matrices, escape sequences, and expected behaviors
/// </summary>
public class SpecParser
{
    private readonly string _specDirectory;

    public SpecParser(string specDirectory)
    {
        _specDirectory = specDirectory ?? throw new ArgumentNullException(nameof(specDirectory));
    }

    /// <summary>
    /// Parses all spec documents in the TDV2200 directory
    /// </summary>
    public SpecDatabase ParseAllSpecs()
    {
        var database = new SpecDatabase();

        // Parse escape sequence table
        var escapeSeqFile = Path.Combine(_specDirectory, "Testing2200_9S", "tdv2200_escape_sequence_table.md");
        if (File.Exists(escapeSeqFile))
        {
            ParseEscapeSequenceTable(escapeSeqFile, database);
        }

        // Parse terminal analysis
        var analysisFile = Path.Combine(_specDirectory, "Testing2200_9S", "nd_terminal_analysis_updated.md");
        if (File.Exists(analysisFile))
        {
            ParseTerminalAnalysis(analysisFile, database);
        }

        // Parse TDV2200 main document
        var mainFile = Path.Combine(_specDirectory, "Term and keyboard info", "TDV2200.MD");
        if (File.Exists(mainFile))
        {
            ParseMainDocument(mainFile, database);
        }

        return database;
    }

    private void ParseEscapeSequenceTable(string filePath, SpecDatabase database)
    {
        var content = File.ReadAllText(filePath);

        // Extract escape sequences from markdown tables
        var lines = content.Split('\n');

        foreach (var line in lines)
        {
            if (line.Contains("|") && !line.StartsWith("|--"))
            {
                var parts = line.Split('|').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToArray();
                if (parts.Length >= 4)
                {
                    var sequence = parts[0];
                    var hex = parts.Length > 1 ? parts[1] : "";
                    var function = parts.Length > 2 ? parts[2] : "";
                    var implementation = parts.Length > 3 ? parts[3] : "";

                    if (sequence.StartsWith("ESC") || sequence.StartsWith("CSI") || sequence.Contains("\\x1b"))
                    {
                        database.AddEscapeSequence(new EscapeSequenceSpec
                        {
                            Sequence = sequence,
                            HexValue = hex,
                            Function = function,
                            ImplementationStatus = implementation,
                            SourceFile = Path.GetFileName(filePath)
                        });
                    }
                }
            }
        }
    }

    private void ParseTerminalAnalysis(string filePath, SpecDatabase database)
    {
        var content = File.ReadAllText(filePath);

        // Extract ND private sequences
        var ndSequencePattern = @"CSI\s+(\d+)\s+([a-z])\s*\|\s*(\d+)\s*\|\s*0x([0-9A-F]+)";
        var matches = Regex.Matches(content, ndSequencePattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            var code = match.Groups[1].Value;
            var final = match.Groups[2].Value;
            var decimalValue = match.Groups[3].Value;
            var hexValue = match.Groups[4].Value;

            database.AddEscapeSequence(new EscapeSequenceSpec
            {
                Sequence = $"ESC [{code}{final}",
                HexValue = $"0x{hexValue}",
                Function = $"ND Private CSI {code}",
                SourceFile = Path.GetFileName(filePath)
            });
        }
    }

    private void ParseMainDocument(string filePath, SpecDatabase database)
    {
        var content = File.ReadAllText(filePath);

        // Extract query/response patterns
        var lines = content.Split('\n');

        foreach (var line in lines)
        {
            if (line.Contains("CSI") && (line.Contains("c") || line.Contains("n") || line.Contains("Z")))
            {
                // Extract query sequences
                var queryMatch = Regex.Match(line, @"CSI\s+(\d+)\s+([cnZ])");
                if (queryMatch.Success)
                {
                    database.AddQueryResponse(new QueryResponseSpec
                    {
                        Query = $"ESC [{queryMatch.Groups[1].Value}{queryMatch.Groups[2].Value}",
                        SourceFile = Path.GetFileName(filePath)
                    });
                }
            }
        }
    }
}

/// <summary>
/// Specification database storing parsed spec information
/// </summary>
public class SpecDatabase
{
    private readonly List<EscapeSequenceSpec> _escapeSequences = new();
    private readonly List<QueryResponseSpec> _queryResponses = new();
    private readonly Dictionary<string, FeatureSpec> _features = new();

    public IReadOnlyList<EscapeSequenceSpec> EscapeSequences => _escapeSequences;
    public IReadOnlyList<QueryResponseSpec> QueryResponses => _queryResponses;
    public IReadOnlyDictionary<string, FeatureSpec> Features => _features;

    public void AddEscapeSequence(EscapeSequenceSpec spec)
    {
        _escapeSequences.Add(spec);
    }

    public void AddQueryResponse(QueryResponseSpec spec)
    {
        _queryResponses.Add(spec);
    }

    public void AddFeature(string name, FeatureSpec spec)
    {
        _features[name] = spec;
    }

    public EscapeSequenceSpec? FindEscapeSequence(string sequence)
    {
        return _escapeSequences.FirstOrDefault(e =>
            e.Sequence.Equals(sequence, StringComparison.OrdinalIgnoreCase) ||
            e.Sequence.Contains(sequence, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Specification for an escape sequence
/// </summary>
public class EscapeSequenceSpec
{
    public string Sequence { get; set; } = "";
    public string HexValue { get; set; } = "";
    public string Function { get; set; } = "";
    public string ImplementationStatus { get; set; } = "";
    public string SourceFile { get; set; } = "";
}

/// <summary>
/// Specification for query/response commands
/// </summary>
public class QueryResponseSpec
{
    public string Query { get; set; } = "";
    public string ExpectedResponse { get; set; } = "";
    public string SourceFile { get; set; } = "";
}

/// <summary>
/// Specification for a feature
/// </summary>
public class FeatureSpec
{
    public string Name { get; set; } = "";
    public string Subsystem { get; set; } = "";
    public string SpecReference { get; set; } = "";
    public string ExpectedBehavior { get; set; } = "";
    public string SourceFile { get; set; } = "";
}

