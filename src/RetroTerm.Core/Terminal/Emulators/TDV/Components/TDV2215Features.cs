namespace RetroTerm.Core.Terminal.Emulators.TDV.Components;

/// <summary>
/// TDV2215 Features Composer - Aggregates all TDV2215-specific features
/// Provides a single entry point for TDV2215 functionality that can be injected into any TDV terminal
/// </summary>
public class TDV2215Features
{
    public readonly TDVExtendedModeFeature ExtendedMode;
    public readonly TDVTransparentModeFeature TransparentMode;
    public readonly TDVDCSHandlerFeature DCSHandler;
    public readonly TDVThreeCharacterSequenceFeature ThreeCharacterSequences;

    /// <summary>
    /// Gets whether extended mode is enabled
    /// </summary>
    public bool IsExtendedMode => ExtendedMode.IsEnabled;

    /// <summary>
    /// Gets whether transparent mode is enabled
    /// </summary>
    public bool IsTransparentMode => TransparentMode.IsEnabled;

    public TDV2215Features()
    {
        ExtendedMode = new TDVExtendedModeFeature();
        TransparentMode = new TDVTransparentModeFeature();
        DCSHandler = new TDVDCSHandlerFeature();
        ThreeCharacterSequences = new TDVThreeCharacterSequenceFeature(ExtendedMode, TransparentMode);
    }

    /// <summary>
    /// Reset all TDV2215 features to initial state
    /// </summary>
    public void Reset()
    {
        ExtendedMode.Reset();
        TransparentMode.Reset();
        DCSHandler.Reset();
    }
}

