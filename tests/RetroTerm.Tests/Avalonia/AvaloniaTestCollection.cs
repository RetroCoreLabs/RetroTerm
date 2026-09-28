using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Collection definition for Avalonia tests.
/// Ensures Avalonia tests run sequentially to avoid platform conflicts.
/// </summary>
[CollectionDefinition("Avalonia")]
public class AvaloniaTestCollection : ICollectionFixture<AvaloniaTestFixture>
{
    // This class has no code, and is never created.
    // Its purpose is to be the place to apply [CollectionDefinition]
    // and all the ICollectionFixture<> interfaces.
}

/// <summary>
/// Fixture for Avalonia tests - handles Avalonia initialization
/// </summary>
public class AvaloniaTestFixture : IDisposable
{
    public AvaloniaTestFixture()
    {
        // Initialize Avalonia headless platform if not already done
        // The AvaloniaFact attribute handles this automatically
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}
