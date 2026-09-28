using System;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// Tests for TDV2200 advanced features:
/// - Rectangle attribute operations (NDAAR, NDRAR)
/// - Work area operations (NDLIWA, NDDLWA, NDICHE, NDDCHE)
/// - ISO646 variant character mapping
/// </summary>
public class TDV2200AdvancedFeaturesTests
{
    private readonly TDV2200Emulator _emulator;

    public TDV2200AdvancedFeaturesTests()
    {
        _emulator = new TDV2200Emulator(80, 24);
    }

    #region NDAAR - Add Attribute in Rectangle Tests

    [Fact]
    public void NDAAR_ShouldAddBoldAttribute()
    {
        // Arrange - Write some text first
        _emulator.ProcessData(System.Text.Encoding.UTF8.GetBytes("HELLO"));

        // Act - Send NDAAR: Add Bold (1) in rectangle 0,0 to 0,4
        // Format: ESC[attr;top;left;bottom;right{
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'0', (byte)';', (byte)'0', (byte)';', (byte)'0', (byte)';', (byte)'4', (byte)'{' });

        // Assert - Check that bold was added
        for (int col = 0; col <= 4; col++)
        {
            var cell = _emulator.Buffer.GetCell(0, col);
            Assert.True((cell.Attributes & CharacterAttributes.Bold) != 0, $"Cell at col {col} should have Bold attribute");
        }
    }

    [Fact]
    public void NDAAR_ShouldAddUnderlineAttribute()
    {
        // Arrange
        _emulator.ProcessData(System.Text.Encoding.UTF8.GetBytes("TEST"));

        // Act - Add Underline (4) in rectangle 0,0 to 0,3
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'4', (byte)';', (byte)'0', (byte)';', (byte)'0', (byte)';', (byte)'0', (byte)';', (byte)'3', (byte)'{' });

        // Assert
        for (int col = 0; col <= 3; col++)
        {
            var cell = _emulator.Buffer.GetCell(0, col);
            Assert.True((cell.Attributes & CharacterAttributes.Underline) != 0, $"Cell at col {col} should have Underline attribute");
        }
    }

    [Fact]
    public void NDAAR_ShouldPreserveExistingAttributes()
    {
        // Arrange - Set bold on text
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'m' }); // Set bold
        _emulator.ProcessData(System.Text.Encoding.UTF8.GetBytes("TEXT"));

        // Act - Add underline without removing bold
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'4', (byte)';', (byte)'0', (byte)';', (byte)'0', (byte)';', (byte)'0', (byte)';', (byte)'3', (byte)'{' });

        // Assert - Both bold and underline should be present
        var cell = _emulator.Buffer.GetCell(0, 0);
        Assert.True((cell.Attributes & CharacterAttributes.Bold) != 0, "Bold should be preserved");
        Assert.True((cell.Attributes & CharacterAttributes.Underline) != 0, "Underline should be added");
    }

    #endregion

    #region NDRAR - Remove Attribute in Rectangle Tests

    [Fact]
    public void NDRAR_ShouldRemoveBoldAttribute()
    {
        // Arrange - Set bold text
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'m' }); // Set bold
        _emulator.ProcessData(System.Text.Encoding.UTF8.GetBytes("BOLD"));
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'0', (byte)'m' }); // Reset

        // Verify bold is set
        Assert.True((_emulator.Buffer.GetCell(0, 0).Attributes & CharacterAttributes.Bold) != 0, "Bold should be set initially");

        // Act - Remove Bold (1) from rectangle 0,0 to 0,3
        // Format: ESC[attr;top;left;bottom;right|
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'0', (byte)';', (byte)'0', (byte)';', (byte)'0', (byte)';', (byte)'3', (byte)'|' });

        // Assert - Bold should be removed
        for (int col = 0; col <= 3; col++)
        {
            var cell = _emulator.Buffer.GetCell(0, col);
            Assert.True((cell.Attributes & CharacterAttributes.Bold) == 0, $"Cell at col {col} should not have Bold attribute");
        }
    }

    [Fact]
    public void NDRAR_ShouldPreserveOtherAttributes()
    {
        // Arrange - Set bold and underline
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'4', (byte)'m' }); // Bold + Underline
        _emulator.ProcessData(System.Text.Encoding.UTF8.GetBytes("MIX"));

        // Act - Remove only bold (1)
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'0', (byte)';', (byte)'0', (byte)';', (byte)'0', (byte)';', (byte)'2', (byte)'|' });

        // Assert - Bold removed, underline preserved
        var cell = _emulator.Buffer.GetCell(0, 0);
        Assert.True((cell.Attributes & CharacterAttributes.Bold) == 0, "Bold should be removed");
        Assert.True((cell.Attributes & CharacterAttributes.Underline) != 0, "Underline should be preserved");
    }

    #endregion

    #region NDICHE - Insert Characters with Extent Tests

    [Fact]
    public void NDICHE_ShouldInsertBlankCharacters()
    {
        // Arrange - Write text
        _emulator.ProcessData(System.Text.Encoding.UTF8.GetBytes("ABCDEFGH"));
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'H' }); // Move cursor to home

        // Act - Insert 3 characters at position 0 (ESC[3s)
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'3', (byte)'s' });

        // Assert - First 3 chars should be spaces, rest shifted
        Assert.True(_emulator.Buffer.GetCell(0, 0).IsEmpty);
        Assert.True(_emulator.Buffer.GetCell(0, 1).IsEmpty);
        Assert.True(_emulator.Buffer.GetCell(0, 2).IsEmpty);
        Assert.Equal('A', (char)_emulator.Buffer.GetCell(0, 3).Codepoint);
        Assert.Equal('B', (char)_emulator.Buffer.GetCell(0, 4).Codepoint);
    }

    [Fact]
    public void NDICHE_DefaultsToOneCharacter()
    {
        // Arrange
        _emulator.ProcessData(System.Text.Encoding.UTF8.GetBytes("TEST"));
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'H' }); // Move cursor to home

        // Act - Insert with no parameter (default 1)
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'s' });

        // Assert - First char should be space
        Assert.True(_emulator.Buffer.GetCell(0, 0).IsEmpty);
        Assert.Equal('T', (char)_emulator.Buffer.GetCell(0, 1).Codepoint);
    }

    #endregion

    #region NDDCHE - Delete Characters with Extent Tests

    [Fact]
    public void NDDCHE_ShouldDeleteCharacters()
    {
        // Arrange - Write text
        _emulator.ProcessData(System.Text.Encoding.UTF8.GetBytes("ABCDEFGH"));
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'H' }); // Move cursor to home

        // Act - Delete 3 characters (ESC[3t)
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'3', (byte)'t' });

        // Assert - DEF should now be at start
        Assert.Equal('D', (char)_emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal('E', (char)_emulator.Buffer.GetCell(0, 1).Codepoint);
        Assert.Equal('F', (char)_emulator.Buffer.GetCell(0, 2).Codepoint);
    }

    [Fact]
    public void NDDCHE_DefaultsToOneCharacter()
    {
        // Arrange
        _emulator.ProcessData(System.Text.Encoding.UTF8.GetBytes("TEST"));
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'H' }); // Move cursor to home

        // Act - Delete with no parameter (default 1)
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'[', (byte)'t' });

        // Assert - EST at start
        Assert.Equal('E', (char)_emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal('S', (char)_emulator.Buffer.GetCell(0, 1).Codepoint);
        Assert.Equal('T', (char)_emulator.Buffer.GetCell(0, 2).Codepoint);
    }

    #endregion

    #region TDV Bitmap Font Character Tests
    // TDV terminals use bitmap fonts - NO Unicode/ISO646 mapping.
    // Character codes go directly to the buffer.
    // The bitmap font renders national characters at specific ASCII positions.

    [Fact]
    public void BitmapFont_AllCharacters_ShouldNotMap()
    {
        // TDV with bitmap font: character codes go directly to buffer, no mapping
        _emulator.ProcessData(System.Text.Encoding.UTF8.GetBytes("[\\]@~{|}"));

        // All characters stored unchanged
        Assert.Equal('[', (char)_emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal('\\', (char)_emulator.Buffer.GetCell(0, 1).Codepoint);
        Assert.Equal(']', (char)_emulator.Buffer.GetCell(0, 2).Codepoint);
        Assert.Equal('@', (char)_emulator.Buffer.GetCell(0, 3).Codepoint);
        Assert.Equal('~', (char)_emulator.Buffer.GetCell(0, 4).Codepoint);
        Assert.Equal('{', (char)_emulator.Buffer.GetCell(0, 5).Codepoint);
        Assert.Equal('|', (char)_emulator.Buffer.GetCell(0, 6).Codepoint);
        Assert.Equal('}', (char)_emulator.Buffer.GetCell(0, 7).Codepoint);
    }

    [Fact]
    public void BitmapFont_RegularLetters_ShouldNotMap()
    {
        // Arrange - Set Norwegian variant
        _emulator.CharacterSetVariant = (int)TDV2200ISO646Variant.Norwegian;

        // Act - Write regular letters (should not be mapped)
        _emulator.ProcessData(System.Text.Encoding.UTF8.GetBytes("ABC123"));

        // Assert - Letters unchanged
        Assert.Equal('A', (char)_emulator.Buffer.GetCell(0, 0).Codepoint);
        Assert.Equal('B', (char)_emulator.Buffer.GetCell(0, 1).Codepoint);
        Assert.Equal('C', (char)_emulator.Buffer.GetCell(0, 2).Codepoint);
        Assert.Equal('1', (char)_emulator.Buffer.GetCell(0, 3).Codepoint);
        Assert.Equal('2', (char)_emulator.Buffer.GetCell(0, 4).Codepoint);
        Assert.Equal('3', (char)_emulator.Buffer.GetCell(0, 5).Codepoint);
    }

    [Fact]
    public void ISO646_VariantSelectionViaEscapeSequence_ShouldWork()
    {
        // Arrange - Initial variant should be International (default per TDV2115 spec 9.1.1)
        Assert.Equal(TDV2200ISO646Variant.International, _emulator.CurrentISO646Variant);

        // Act - Send ESC % S to select Swedish variant
        _emulator.ProcessData(new byte[] { 0x1B, (byte)'%', (byte)'S' });

        // Assert
        Assert.Equal(TDV2200ISO646Variant.Swedish, _emulator.CurrentISO646Variant);
    }

    [Fact]
    public void ISO646_AllVariants_ShouldBeSelectable()
    {
        // Test all variant selection sequences per TDV2115 spec section 9.1
        var variants = new (char code, TDV2200ISO646Variant expected)[]
        {
            ('I', TDV2200ISO646Variant.International),
            ('N', TDV2200ISO646Variant.Norwegian),
            ('S', TDV2200ISO646Variant.Swedish),
            ('G', TDV2200ISO646Variant.German)
        };

        for (int i = 0; i < variants.Length; i++)
        {
            var (code, expected) = variants[i];
            // Send ESC % X
            _emulator.ProcessData(new byte[] { 0x1B, (byte)'%', (byte)code });
            Assert.Equal(expected, _emulator.CurrentISO646Variant);
        }
    }

    #endregion

    #region TDVCharacterSets Static Method Tests

    [Fact]
    public void TDVCharacterSets_IsISO646VariantPosition_ShouldIdentifyCorrectPositions()
    {
        // Characters that should be mapped
        Assert.True(TDVCharacterSets.IsISO646VariantPosition('@'));
        Assert.True(TDVCharacterSets.IsISO646VariantPosition('['));
        Assert.True(TDVCharacterSets.IsISO646VariantPosition('\\'));
        Assert.True(TDVCharacterSets.IsISO646VariantPosition(']'));
        Assert.True(TDVCharacterSets.IsISO646VariantPosition('^'));
        Assert.True(TDVCharacterSets.IsISO646VariantPosition('`'));
        Assert.True(TDVCharacterSets.IsISO646VariantPosition('{'));
        Assert.True(TDVCharacterSets.IsISO646VariantPosition('|'));
        Assert.True(TDVCharacterSets.IsISO646VariantPosition('}'));
        Assert.True(TDVCharacterSets.IsISO646VariantPosition('~'));
        Assert.True(TDVCharacterSets.IsISO646VariantPosition('#'));

        // Characters that should not be mapped
        Assert.False(TDVCharacterSets.IsISO646VariantPosition('A'));
        Assert.False(TDVCharacterSets.IsISO646VariantPosition('Z'));
        Assert.False(TDVCharacterSets.IsISO646VariantPosition('0'));
        Assert.False(TDVCharacterSets.IsISO646VariantPosition('9'));
        Assert.False(TDVCharacterSets.IsISO646VariantPosition(' '));
    }

    [Fact]
    public void TDVCharacterSets_ApplyISO646Variant_ShouldMapCorrectly()
    {
        // Norwegian variant
        Assert.Equal('Æ', TDVCharacterSets.ApplyISO646Variant(TDV2200ISO646Variant.Norwegian, '['));
        Assert.Equal('Ø', TDVCharacterSets.ApplyISO646Variant(TDV2200ISO646Variant.Norwegian, '\\'));
        Assert.Equal('Å', TDVCharacterSets.ApplyISO646Variant(TDV2200ISO646Variant.Norwegian, ']'));

        // Swedish variant
        Assert.Equal('Ä', TDVCharacterSets.ApplyISO646Variant(TDV2200ISO646Variant.Swedish, '['));
        Assert.Equal('Ö', TDVCharacterSets.ApplyISO646Variant(TDV2200ISO646Variant.Swedish, '\\'));

        // International variant (no mapping)
        Assert.Equal('[', TDVCharacterSets.ApplyISO646Variant(TDV2200ISO646Variant.International, '['));
        Assert.Equal('\\', TDVCharacterSets.ApplyISO646Variant(TDV2200ISO646Variant.International, '\\'));
    }

    #endregion
}
