using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RetroTerm.Desktop;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The windows carry the solid RetroTerm logo, and the retired "Dark CRT" logo is gone.
/// </summary>
/// <remarks>
/// Ronny, 28 September 2026: "we have new icons - and are not using RetroTerm-DarkCRT, use
/// RetroTerm-solid-512.png and get rid of the DarkCRT". Until then the main window and the
/// popout window named a 48 px RetroTerm-icon.png, the README showed the Dark CRT bezel, and
/// both Dark CRT files still shipped inside the executable as Avalonia resources.
/// </remarks>
[Collection("Avalonia")]
public class WindowIconIsTheSolidLogoTests
{
    private static readonly Uri SolidLogo = new("avares://RetroTerm.Desktop/Assets/RetroTerm-solid-512.png");

    [AvaloniaFact]
    public void TheSolidLogoShipsInsideTheExecutableAt512Pixels()
    {
        Assert.True(AssetLoader.Exists(SolidLogo), "RetroTerm-solid-512.png is not an Avalonia resource");
        using var stream = AssetLoader.Open(SolidLogo);
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
    public void NoDarkCrtFileIsShippedAnyMore()
    {
        var assets = AssetLoader.GetAssets(new Uri("avares://RetroTerm.Desktop/Assets/"), null);
        var list = new List<Uri>(assets);
        for (int i = 0; i < list.Count; i++)
        {
            Assert.DoesNotContain("DarkCRT", list[i].ToString());
            Assert.DoesNotContain("RetroTerm-icon.png", list[i].ToString());
        }
    }
}
