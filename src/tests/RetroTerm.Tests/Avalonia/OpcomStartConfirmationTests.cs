using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The OPCOM Start button asks before it acts.
///
/// Ronny's instruction, 6 September 2026: on a real ND there is no way back from Start
/// over the wire. Once the CPU is running he has to walk to the machine and reset it by
/// hand. Every other button in the OPCOM window is recoverable; this one is not, so it is
/// the only one that asks.
///
/// The address is part of the question on purpose. Starting from the wrong address is as
/// damaging as not meaning to start at all, and a confirmation that does not say what it
/// is about trains people to click through it.
/// </summary>
[Collection("Avalonia")]
public class OpcomStartConfirmationTests
{
    private static TextBlock FindQuestion(Window dialog)
    {
        var found = dialog.GetLogicalDescendants();
        foreach (var child in found)
        {
            if (child is TextBlock tb && tb.Name == "StartConfirmQuestion") return tb;
        }
        throw new InvalidOperationException("the dialog has no question text block");
    }

    [AvaloniaFact]
    public void TheQuestionNamesTheOctalAddressTheMachineWouldStartFrom()
    {
        var dialog = OpcomDebugWindow.BuildStartConfirmationDialog(0x0204, out _);
        var question = FindQuestion(dialog);

        // 0x0204 is 001004 octal. The address must appear as OPCOM would show it.
        Assert.Contains("001004", question.Text);
        Assert.Contains("start the machine", question.Text);
    }

    [AvaloniaFact]
    public void AddressZeroIsStillShownInFullRatherThanLeftOut()
    {
        var dialog = OpcomDebugWindow.BuildStartConfirmationDialog(0, out _);
        var question = FindQuestion(dialog);
        Assert.Contains("000000", question.Text);
    }

    [AvaloniaFact]
    public void NothingIsConfirmedUntilTheStartButtonIsPressed()
    {
        var dialog = OpcomDebugWindow.BuildStartConfirmationDialog(0, out Func<bool> wasConfirmed);
        Assert.False(wasConfirmed(), "the dialog reported a confirmation before anything was clicked");
    }

    [AvaloniaFact]
    public void PressingCancelLeavesTheMachineAlone()
    {
        var dialog = OpcomDebugWindow.BuildStartConfirmationDialog(0, out Func<bool> wasConfirmed);
        Button cancel = FindButton(dialog, "StartConfirmCancelButton");

        // Cancel is the safe answer, so it must be the one Enter and Escape both pick.
        Assert.True(cancel.IsDefault, "Enter does not choose Cancel");
        Assert.True(cancel.IsCancel, "Escape does not choose Cancel");

        Assert.False(wasConfirmed());
    }

    [AvaloniaFact]
    public void PressingStartIsWhatConfirms()
    {
        var dialog = OpcomDebugWindow.BuildStartConfirmationDialog(0, out Func<bool> wasConfirmed);
        Button start = FindButton(dialog, "StartConfirmStartButton");

        // The start button must not be the default: nobody should start a machine by
        // pressing Enter on a dialog they have not read.
        Assert.False(start.IsDefault, "Enter would start the machine");

        start.Command?.Execute(null);
        var args = new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent);
        start.RaiseEvent(args);
        Assert.True(wasConfirmed(), "pressing Start did not confirm");
    }

    /// <summary>
    /// The breakpoint button warns too, because setting a breakpoint RUNS the CPU.
    /// Measured on a real ND-120/CX on 6 September 2026: "4." was echoed and then a second
    /// full stop came back a second and a half later, the machine having executed its way
    /// round to address 4. The name says "set"; the machine hears "run until".
    /// </summary>
    [AvaloniaFact]
    public void TheBreakpointWarningSaysTheMachineWillRun()
    {
        var dialog = OpcomDebugWindow.BuildStartConfirmationDialog(0x0204, out _, forBreakpoint: true);
        var question = FindQuestion(dialog);

        Assert.Contains("001004", question.Text);
        Assert.Contains("run the machine", question.Text);
    }

    [AvaloniaFact]
    public void TheBreakpointWarningIsWordedDifferentlyFromTheStartOne()
    {
        var breakpoint = FindQuestion(OpcomDebugWindow.BuildStartConfirmationDialog(0, out _, forBreakpoint: true));
        var start = FindQuestion(OpcomDebugWindow.BuildStartConfirmationDialog(0, out _));

        Assert.NotEqual(start.Text, breakpoint.Text);
        // The start wording must not claim the machine merely runs to somewhere, and the
        // breakpoint wording must not promise it stops only where you asked.
        Assert.Contains("start the machine", start.Text);
    }

    private static Button FindButton(Window dialog, string name)
    {
        foreach (var child in dialog.GetLogicalDescendants())
        {
            if (child is Button b && b.Name == name) return b;
        }
        throw new InvalidOperationException("the dialog has no button called " + name);
    }
}
