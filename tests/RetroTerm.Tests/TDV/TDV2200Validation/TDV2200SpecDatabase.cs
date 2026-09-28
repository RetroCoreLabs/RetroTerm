using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RetroTerm.Tests.TDV.TDV2200Validation;

/// <summary>
/// Specification reference database - indexes all spec documents and maps features to spec sections
/// </summary>
public class TDV2200SpecDatabase
{
    private readonly SpecDatabase _database;
    private readonly string _specDirectory;

    public TDV2200SpecDatabase(string specDirectory)
    {
        _specDirectory = specDirectory ?? throw new ArgumentNullException(nameof(specDirectory));
        var parser = new SpecParser(specDirectory);
        _database = parser.ParseAllSpecs();
    }

    /// <summary>
    /// Gets expected behavior for a feature
    /// </summary>
    public string? GetExpectedBehavior(string featureName)
    {
        if (_database.Features.TryGetValue(featureName, out var feature))
        {
            return feature.ExpectedBehavior;
        }
        return null;
    }

    /// <summary>
    /// Gets spec reference for a feature
    /// </summary>
    public string? GetSpecReference(string featureName)
    {
        if (_database.Features.TryGetValue(featureName, out var feature))
        {
            return feature.SpecReference;
        }
        return null;
    }

    /// <summary>
    /// Finds escape sequence specification
    /// </summary>
    public EscapeSequenceSpec? FindEscapeSequence(string sequence)
    {
        return _database.FindEscapeSequence(sequence);
    }

    /// <summary>
    /// Gets all escape sequences for a subsystem
    /// </summary>
    public IEnumerable<EscapeSequenceSpec> GetEscapeSequencesForSubsystem(string subsystem)
    {
        return _database.EscapeSequences.Where(e =>
            e.Function.Contains(subsystem, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets all query/response specifications
    /// </summary>
    public IEnumerable<QueryResponseSpec> GetQueryResponses()
    {
        return _database.QueryResponses;
    }

    /// <summary>
    /// Gets expected response for a query
    /// </summary>
    public string? GetExpectedResponse(string query)
    {
        var spec = _database.QueryResponses.FirstOrDefault(q =>
            q.Query.Equals(query, StringComparison.OrdinalIgnoreCase));
        return spec?.ExpectedResponse;
    }
}

