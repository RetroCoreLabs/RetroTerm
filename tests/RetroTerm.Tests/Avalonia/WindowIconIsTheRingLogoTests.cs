using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RetroTerm.Desktop;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The windows carry the ring RetroTerm logo, and no file from a retired look ships.
/// </summary>
/// <remarks>
/// Two looks were retired. The "Dark CRT" bezel went on 28 September 2026. A "solid" variant
/// without the ring went on 30 September 2026; it had been wired in as the window icon and, for
/// one release, as the executable icon; it is not the logo Ronny wanted. The logo is the ring set
/// in the assets folder: RetroTerm.svg, RetroTerm-512.png, RetroTerm.ico. These tests pin that
/// the 512 px PNG is packed into the executable at that size, that both windows have an icon,
/// and that neither retired look is packed in any more.
/// </remarks>
[Collection("Avalonia")]
public class WindowIconIsTheRingLogoTests
{
    private static readonly Uri RingLogo = new("avares://RetroTerm.Desktop/Assets/RetroTerm-512.png");

    [AvaloniaFact]
    public void TheRingLogoShipsInsideTheExecutableAt512Pixels()
    {
        Assert.True(AssetLoader.Exists(RingLogo), "RetroTerm-512.png is not an Avalonia resource");
        using var stream = AssetLoader.Open(RingLogo);
        var bitmap = new Bitmap(stream);
        Assert.Equal(512, bitmap.PixelSize.Width);
        Assert.Equal(512, bitmap.PixelSize.Height);
    }

    [AvaloniaFact]
    public void TheMainWindowAndThePopoutWindowBothHaveAnIcon()
    {
        var main = new MainWindow();
        Assert.NotNull(main.Icon);
        main.Close();

        var popout = new TerminalPopoutWindow();
        Assert.NotNull(popout.Icon);
        popout.Close();
    }

    [AvaloniaFact]
    public void NoFileFromARetiredLookIsShippedAnyMore()
    {
        var assets = AssetLoader.GetAssets(new Uri("avares://RetroTerm.Desktop/Assets/"), null);
        var list = new List<Uri>(assets);
        for (int i = 0; i < list.Count; i++)
        {
            string name = list[i].ToString();
            Assert.DoesNotContain("DarkCRT", name);
            Assert.DoesNotContain("RetroTerm-solid", name);
            Assert.DoesNotContain("RetroTerm-icon.png", name);
        }
    }
}
