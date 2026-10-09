using System.Globalization;
using RetroTerm.Core.Protocols.WebSocket.Gateway;

namespace RetroTerm.Desktop.Helpers;

/// <summary>
/// Formats the lines of the terminal list a gateway connection prints in the terminal.
/// </summary>
public static class GatewayMenuFormatter
{
    /// <summary>
    /// One line of the terminal list: the number to type, the emulator's label, the SINTRAN logical
    /// device when there is one, and <c>[in use]</c> when another tab holds the terminal.
    /// </summary>
    /// <param name="number">
    /// The number the user types to pick this terminal, counted from 1.
    /// </param>
    /// <param name="terminal">
    /// The terminal as the emulator registered it.
    /// </param>
    /// <param name="inUse">
    /// True when another tab is connected to this terminal.
    /// </param>
    /// <returns>
    /// The line, without a line ending.
    /// </returns>
    /// <remarks>
    /// A logical device below zero means "none". The standalone ND-500 running NDIX has no ND-100 and
    /// no SINTRAN, so the emulator registers <c>-1</c> for its ttys (nd100x, emu-worker.js,
    /// buildGatewayTerminalList). Printing it showed "device -1" (8 October 2026), so the device is
    /// left out when it is negative.
    /// </remarks>
    public static string TerminalLine(int number, GatewayTerminalInfo terminal, bool inUse)
    {
        string device = terminal.LogicalDevice >= 0
            ? " device " + terminal.LogicalDevice.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        string status = inUse ? " [in use]" : string.Empty;

        // TrimEnd: the name is padded so the "device" column lines up, which leaves trailing spaces
        // when there is neither a device nor a status.
        return ("  " + number.ToString(CultureInfo.InvariantCulture).PadLeft(2) + ". "
            + terminal.Name.PadRight(12) + device + status).TrimEnd();
    }
}
