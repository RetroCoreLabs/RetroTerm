using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The virtual keyboard's key-binding popup can always be closed from the keyboard.
/// </summary>
/// <remarks>
/// Ronny, 27 September 2026: "the right click got stuck in a popup with no visible text." The
/// popup is deliberately not light-dismissed, because an Alt press during a key capture would
/// otherwise close it; Escape only cancelled a capture; and on the old white panel the Close
/// button and the text were unreadable. So there was no way out. Escape now closes the popup
/// when no capture is running, and a second Escape closes it after a capture was cancelled.
/// </remarks>
[Collection("Avalonia")]
public class EscapeClosesTheKeyBindingPopupTests
{
    private static async Task Pump()
    {
        for (int i = 0; i < 6; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(15);
        }
    }

    [AvaloniaFact]
    public async Task EscapeClosesTheOpenPopup()
    {
        var window = new VirtualKeyboardWindow();
        window.Show();
        await Pump();
        var panel = window.PanelForTesting;
        Assert.NotNull(panel);

        Assert.True(panel!.OpenBindingPopupForTesting("G53"), "HJELP (G53) is bindable, so its popup must open");
        Assert.True(panel.IsBindingPopupOpen);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await Pump();

        Assert.False(panel.IsBindingPopupOpen, "Escape must close the binding popup");
        window.Close();
    }

    [AvaloniaFact]
    public async Task EscapeDuringACaptureCancelsTheCaptureAndASecondEscapeCloses()
    {
        var window = new VirtualKeyboardWindow();
        window.Show();
        await Pump();
        var panel = window.PanelForTesting!;
        Assert.True(panel.OpenBindingPopupForTesting("G53"));
        panel.BeginCaptureForTesting();
        Assert.True(panel.IsCapturingKey);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await Pump();
        Assert.False(panel.IsCapturingKey, "the first Escape cancels the capture");
        Assert.True(panel.IsBindingPopupOpen, "and leaves the popup up, as it always did");

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await Pump();
        Assert.False(panel.IsBindingPopupOpen, "the second Escape closes the popup");
        window.Close();
    }
}
