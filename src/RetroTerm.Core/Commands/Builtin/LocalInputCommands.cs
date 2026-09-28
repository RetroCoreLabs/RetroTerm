using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Session;

namespace RetroTerm.Core.Commands.Builtin;

/// <summary>
/// Gives one keystroke to the terminal WINDOW rather than to the host.
/// </summary>
/// <param name="session">
/// The session whose window should receive the key.
/// </param>
/// <param name="key">
/// A key name (Left, Right, Up, Down, Enter, Escape, F1) or a single character to type.
/// </param>
/// <param name="shift">
/// Whether shift is held. ReGIS graphics input reads this: a shifted arrow moves ten pixels
/// instead of one.
/// </param>
/// <returns>
/// Null when the key was delivered, or a sentence saying why it was not.
/// </returns>
/// <remarks>
/// A named delegate supplied by the desktop layer, because only the desktop owns a window with a
/// key path in it. Core stays free of any dependency on Avalonia.
/// </remarks>
public delegate string? LocalKeyDelivery(TerminalSession session, string key, bool shift);

/// <summary>
/// Reads, sets or steps the display zoom of one session's view.
/// </summary>
/// <param name="session">
/// The session whose view to zoom.
/// </param>
/// <param name="percent">
/// The percentage to set, or null to leave it alone.
/// </param>
/// <param name="step">
/// Steps up (positive) or down (negative) the shared ladder; zero to leave it alone.
/// </param>
/// <param name="nowPercent">
/// The zoom in force after the change, which is what the caller wanted to know.
/// </param>
/// <returns>
/// Null when the zoom was read or changed, or a sentence saying why it was not.
/// </returns>
public delegate string? DisplayZoomChange(TerminalSession session, int? percent, int step, out int nowPercent);

/// <summary>
/// Pastes text into one session the way the Paste menu item does.
/// </summary>
/// <param name="session">
/// The session to paste into.
/// </param>
/// <param name="text">
/// The text to paste, or null to use whatever is on the system clipboard.
/// </param>
/// <returns>
/// Null when the paste happened, or a sentence saying why it did not.
/// </returns>
public delegate string? LocalPaste(TerminalSession session, string? text);

/// <summary>
/// LOCALKEY - a keystroke that goes to the terminal window, not down the wire.
/// </summary>
/// <remarks>
/// <para><b>Why this exists, 27 August 2026</b></para>
/// <c>SEND</c>, <c>SENDRAW</c> and <c>SENDKEY</c> all end at
/// <c>TerminalSession.SendInputAsync</c>, which puts bytes on the connection. That covers
/// everything the HOST can see and nothing the WINDOW does on its own - and some of what the
/// window does on its own is the behaviour under test.
/// <para><b>What is only reachable this way</b></para>
/// ReGIS one-shot graphics input is the sharp case. Chapter 15 of the VT330/VT340 Programmer
/// Reference has the arrow keys moving the input cursor and any other key answering the host with
/// a position report; all of that is decided in the terminal, so an arrow sent down the wire moves
/// nothing. The zoom shortcuts (control and plus, minus, zero) are the same shape.
/// <para><b>A character or a key name</b></para>
/// One printable character is typed as text, because a position report has to carry the character
/// the operator actually pressed and only the keyboard layout knows what a key produces. Anything
/// longer is a key name, resolved by the desktop layer.
/// </remarks>
public sealed class LocalKeyCommand : ISessionCommand
{
    private readonly LocalKeyDelivery _deliver;

    /// <summary>
    /// Builds the command around the desktop's key path.
    /// </summary>
    /// <param name="deliver">
    /// What actually hands the key to the window.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="deliver"/> is null.
    /// </exception>
    public LocalKeyCommand(LocalKeyDelivery deliver)
    {
        _deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
    }

    /// <summary>
    /// The command's name in the script DSL.
    /// </summary>
    public string Name => "LOCALKEY";

    /// <summary>
    /// One line for the generated help.
    /// </summary>
    public string Summary => "Press a key AT THE TERMINAL rather than sending it to the host - "
        + "the ReGIS graphics input cursor and the zoom shortcuts are only reachable this way";

    /// <summary>
    /// A usable example for the generated help.
    /// </summary>
    public string Example => "LOCALKEY key=Left shift=true";

    /// <summary>
    /// Whether the command writes a line of output.
    /// </summary>
    public bool ProducesOutput => true;

    /// <summary>
    /// The parameters, read by the script parser and the MCP tool list alike.
    /// </summary>
    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("key", CommandParameterType.String, required: true, defaultValue: null,
            "Key name (Left, Right, Up, Down, Enter, Escape, F1, Add, Subtract, D0) or a single "
            + "character to type"),
        new CommandParameter("shift", CommandParameterType.Bool, required: false, defaultValue: "false",
            "Hold shift - a shifted arrow moves the ReGIS input cursor ten pixels instead of one")
    };

    /// <summary>
    /// Hands the key to the window.
    /// </summary>
    /// <param name="session">
    /// The session whose window receives it.
    /// </param>
    /// <param name="args">
    /// The parsed arguments.
    /// </param>
    /// <param name="cancellationToken">
    /// Not used - a keystroke is immediate.
    /// </param>
    /// <returns>
    /// What was pressed, or why it could not be.
    /// </returns>
    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args,
        CancellationToken cancellationToken)
    {
        var key = args.GetString("key")!;
        if (string.IsNullOrEmpty(key))
        {
            return Task.FromResult(CommandResult.Fail("key is empty"));
        }

        bool shift = args.GetBool("shift", false);

        var problem = _deliver(session, key, shift);
        if (problem != null)
        {
            return Task.FromResult(CommandResult.Fail(problem));
        }

        return Task.FromResult(CommandResult.Ok(shift
            ? "pressed shift+" + key + " at the terminal"
            : "pressed " + key + " at the terminal"));
    }

    /// <summary>
    /// Adds the command to a registry, which gives both the script DSL and MCP the same thing.
    /// </summary>
    /// <param name="registry">
    /// The registry to add to.
    /// </param>
    /// <param name="deliver">
    /// The desktop's key path.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="registry"/> is null.
    /// </exception>
    public static void RegisterAll(CommandRegistry registry, LocalKeyDelivery deliver)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        registry.Register(new LocalKeyCommand(deliver));
    }
}

/// <summary>
/// ZOOM - read, set or step the magnification of one session's view.
/// </summary>
/// <remarks>
/// <para><b>Why a command and not a preference</b></para>
/// The zoom is a property of the VIEW, not of the connection, and it changes what the reader sees
/// without changing a single byte on the wire. Until this command existed the only ways to reach it
/// were the keyboard shortcut and the dropdown, so anything driving the terminal could neither set
/// it nor find out what it was - and the 300 percent case had to be judged by a person.
/// <para><b>The ladder is shared</b></para>
/// Stepping walks <c>TerminalCanvas.ZoomSteps</c>, the same 50 to 400 ladder the dropdown offers,
/// so a script and a reader cannot end up on different numbers. Setting a percentage is not
/// restricted to the ladder, because the value is a magnification and any of them is meaningful.
/// </remarks>
public sealed class ZoomCommand : ISessionCommand
{
    private readonly DisplayZoomChange _zoom;

    /// <summary>
    /// Builds the command around the desktop's view.
    /// </summary>
    /// <param name="zoom">
    /// What actually reads and changes the magnification.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="zoom"/> is null.
    /// </exception>
    public ZoomCommand(DisplayZoomChange zoom)
    {
        _zoom = zoom ?? throw new ArgumentNullException(nameof(zoom));
    }

    /// <summary>
    /// The command's name in the script DSL.
    /// </summary>
    public string Name => "ZOOM";

    /// <summary>
    /// One line for the generated help.
    /// </summary>
    public string Summary => "Read, set or step the display zoom (50 to 400 percent); no arguments reports it";

    /// <summary>
    /// A usable example for the generated help.
    /// </summary>
    public string Example => "ZOOM percent=300";

    /// <summary>
    /// Whether the command writes a line of output.
    /// </summary>
    public bool ProducesOutput => true;

    /// <summary>
    /// The parameters, read by the script parser and the MCP tool list alike.
    /// </summary>
    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("percent", CommandParameterType.Int, required: false, defaultValue: null,
            "Magnification to set, 50 to 400; 100 is the normal view and hands sizing back to the window"),
        new CommandParameter("step", CommandParameterType.Int, required: false, defaultValue: null,
            "Move this many places up (positive) or down (negative) the shared zoom ladder")
    };

    /// <summary>
    /// Reads or changes the zoom.
    /// </summary>
    /// <param name="session">
    /// The session whose view to act on.
    /// </param>
    /// <param name="args">
    /// The parsed arguments.
    /// </param>
    /// <param name="cancellationToken">
    /// Not used - a zoom change is immediate.
    /// </param>
    /// <returns>
    /// The zoom now in force, or why it could not be changed.
    /// </returns>
    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args,
        CancellationToken cancellationToken)
    {
        bool hasPercent = args.Contains("percent");
        bool hasStep = args.Contains("step");

        // Both at once is a script bug, not something to guess at. Silently letting one win would
        // make a mistyped script do the wrong thing quietly, which is the worst of the outcomes.
        if (hasPercent && hasStep)
        {
            return Task.FromResult(CommandResult.Fail(
                "give percent or step, not both - they would fight over the same value"));
        }

        int? percent = hasPercent ? args.GetInt("percent", 0) : (int?)null;
        int step = hasStep ? args.GetInt("step", 0) : 0;

        var problem = _zoom(session, percent, step, out int now);
        if (problem != null)
        {
            return Task.FromResult(CommandResult.Fail(problem));
        }

        return Task.FromResult(CommandResult.Ok(
            "zoom is " + now.ToString(CultureInfo.InvariantCulture) + " percent"));
    }

    /// <summary>
    /// Adds the command to a registry, which gives both the script DSL and MCP the same thing.
    /// </summary>
    /// <param name="registry">
    /// The registry to add to.
    /// </param>
    /// <param name="zoom">
    /// The desktop's view.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="registry"/> is null.
    /// </exception>
    public static void RegisterAll(CommandRegistry registry, DisplayZoomChange zoom)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        registry.Register(new ZoomCommand(zoom));
    }
}

/// <summary>
/// PASTE - put text into the session the way the Paste menu item does.
/// </summary>
/// <remarks>
/// <para><b>Not the same as SEND</b></para>
/// <c>SEND</c> writes bytes to the connection and nothing else. A paste goes through the terminal
/// first, so it picks up two things the wire cannot: the national character conversion for the
/// active ISO 646 variant, and bracketed paste wrapping when the host has asked for it. Testing
/// paste with <c>SEND</c> would test neither.
/// <para><b>Why the text can be given</b></para>
/// Naming the text makes a paste repeatable and leaves the reader's own clipboard alone. Leaving it
/// out uses the system clipboard, which is what a person does.
/// </remarks>
public sealed class PasteCommand : ISessionCommand
{
    private readonly LocalPaste _paste;

    /// <summary>
    /// Builds the command around the desktop's paste path.
    /// </summary>
    /// <param name="paste">
    /// What actually pastes.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="paste"/> is null.
    /// </exception>
    public PasteCommand(LocalPaste paste)
    {
        _paste = paste ?? throw new ArgumentNullException(nameof(paste));
    }

    /// <summary>
    /// The command's name in the script DSL.
    /// </summary>
    public string Name => "PASTE";

    /// <summary>
    /// One line for the generated help.
    /// </summary>
    public string Summary => "Paste text into the session the way the Paste menu does, with national "
        + "characters converted and bracketed paste applied";

    /// <summary>
    /// A usable example for the generated help.
    /// </summary>
    public string Example => "PASTE text=\"LIST-FILES\\r\"";

    /// <summary>
    /// Whether the command writes a line of output.
    /// </summary>
    public bool ProducesOutput => true;

    /// <summary>
    /// The parameters, read by the script parser and the MCP tool list alike.
    /// </summary>
    /// <remarks>
    /// The text carries the backslash escapes, like SEND's does: a pasted line ends in a carriage
    /// return, and a paste that spans lines is the whole point of the wrapped-line case.
    /// </remarks>
    public IReadOnlyList<CommandParameter> Parameters { get; } = new[]
    {
        new CommandParameter("text", CommandParameterType.String, required: false, defaultValue: null,
            "Text to paste; leave it out to paste whatever is on the system clipboard",
            decodeEscapes: true)
    };

    /// <summary>
    /// Pastes.
    /// </summary>
    /// <param name="session">
    /// The session to paste into.
    /// </param>
    /// <param name="args">
    /// The parsed arguments.
    /// </param>
    /// <param name="cancellationToken">
    /// Not used - a paste is immediate.
    /// </param>
    /// <returns>
    /// How much was pasted, or why it could not be.
    /// </returns>
    public Task<CommandResult> ExecuteAsync(TerminalSession session, CommandArgs args,
        CancellationToken cancellationToken)
    {
        var text = args.GetString("text");

        var problem = _paste(session, text);
        if (problem != null)
        {
            return Task.FromResult(CommandResult.Fail(problem));
        }

        return Task.FromResult(CommandResult.Ok(text == null
            ? "pasted the clipboard"
            : "pasted " + text.Length.ToString(CultureInfo.InvariantCulture) + " characters"));
    }

    /// <summary>
    /// Adds the command to a registry, which gives both the script DSL and MCP the same thing.
    /// </summary>
    /// <param name="registry">
    /// The registry to add to.
    /// </param>
    /// <param name="paste">
    /// The desktop's paste path.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="registry"/> is null.
    /// </exception>
    public static void RegisterAll(CommandRegistry registry, LocalPaste paste)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        registry.Register(new PasteCommand(paste));
    }
}

/// <summary>
/// Registers the three commands that act on the terminal WINDOW instead of the connection.
/// </summary>
/// <remarks>
/// They are grouped because they share one cause: each drives something handled inside the
/// terminal, which no amount of sending bytes can reach. Before they existed, the ReGIS graphics
/// input cursor, the zoom and the paste path were the three parts of this program that could only
/// be judged by a person sitting in front of it.
/// </remarks>
public static class LocalInputCommands
{
    /// <summary>
    /// Adds LOCALKEY, ZOOM and PASTE to a registry.
    /// </summary>
    /// <param name="registry">
    /// The registry to add to.
    /// </param>
    /// <param name="deliver">
    /// The desktop's key path.
    /// </param>
    /// <param name="zoom">
    /// The desktop's magnification.
    /// </param>
    /// <param name="paste">
    /// The desktop's paste path.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="registry"/> is null.
    /// </exception>
    public static void RegisterAll(CommandRegistry registry, LocalKeyDelivery deliver,
        DisplayZoomChange zoom, LocalPaste paste)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));

        LocalKeyCommand.RegisterAll(registry, deliver);
        ZoomCommand.RegisterAll(registry, zoom);
        PasteCommand.RegisterAll(registry, paste);
    }
}
