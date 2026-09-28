using System;

namespace RetroTerm.Core.Terminal.Emulators.TDV;

/// <summary>
/// The TDV message lamps, and the three sequences that drive them.
/// </summary>
/// <remarks>
/// <para><b>Four lamps, three operations - it used to be the other way round</b></para>
/// ND Display Terminal 1200 section 5.52, NDSLED, "Light the Message Lamps According to
/// Parameters", gives the lamps as 1 EXPAND, 2 APPEND, 3 BUSY, 4 MESSAGE, with parameter 0 meaning
/// all of them. Sections 5.37 and 5.38 give the other two operations on the same four lamps:
/// NDBLED makes them blink, NDCLED clears them.
///
/// Until 11 September 2026 this class held three booleans named Clear, Set and Blink - the three
/// OPERATIONS modelled as if they were the lamps. Nothing could say which lamp a host meant,
/// because there was nowhere to put it, and the virtual keyboard had to guess: its comment read
/// "this is an interpretation: Blink wins over Set, and an explicit Clear with nothing else
/// asserted means off".
///
/// <para><b>Each lamp is one tri-state, not three flags</b></para>
/// A lamp is off, lit, or blinking. Those are exclusive on the hardware - NDBLED on a lit lamp
/// makes it blink rather than leaving it both - so one state per lamp is the honest shape.
///
/// <para><b>The keyboard lamps of 2115 operation are a SEPARATE object, still</b></para>
/// In 2115-compatible operation the C0 codes light lamps too: TDV 2215 section 3.1, "ENQ - Light 1
/// on keyboard comes on", "ACK - Light 2 on keyboard comes on". Those live in
/// <c>TDV2115CompatibilityHandler</c> and almost certainly drive the same physical lamps as 1 to 3
/// here, but nothing held here says so outright, so the two are not merged. The virtual keyboard
/// shows a lamp lit if either source says it is.
/// </remarks>
public class TDVMessageLEDs
{
    /// <summary>
    /// The number of lamps. Parameter values 1 to 4 address them; 0 means all.
    /// </summary>
    public const int LampCount = 4;

    private readonly TDVMessageLampState[] _lamps = new TDVMessageLampState[LampCount];

    /// <summary>
    /// Raised whenever any lamp changes. Lets the virtual keyboard mirror the terminal's lamps
    /// without polling.
    /// </summary>
    /// <remarks>
    /// Only raised on an ACTUAL change, so a host that re-asserts the same state every screen
    /// refresh does not flood the UI thread with redundant repaints.
    /// </remarks>
    public event Action? StateChanged;

    /// <summary>
    /// Reads one lamp.
    /// </summary>
    /// <param name="lamp">
    /// The lamp number, 1 to 4.
    /// </param>
    /// <returns>
    /// Its state, or <c>Off</c> for a number outside the range.
    /// </returns>
    public TDVMessageLampState GetState(int lamp)
    {
        if (lamp < 1 || lamp > LampCount)
        {
            return TDVMessageLampState.Off;
        }

        return _lamps[lamp - 1];
    }

    /// <summary>
    /// NDSLED - lights a lamp, or all of them.
    /// </summary>
    /// <param name="lamp">
    /// The lamp number, 1 to 4, or 0 for all.
    /// </param>
    public void Light(int lamp)
    {
        Apply(lamp, TDVMessageLampState.Lit);
    }

    /// <summary>
    /// NDBLED - makes a lamp blink, or all of them.
    /// </summary>
    /// <param name="lamp">
    /// The lamp number, 1 to 4, or 0 for all.
    /// </param>
    public void Blink(int lamp)
    {
        Apply(lamp, TDVMessageLampState.Blinking);
    }

    /// <summary>
    /// NDCLED - clears a lamp, or all of them.
    /// </summary>
    /// <param name="lamp">
    /// The lamp number, 1 to 4, or 0 for all.
    /// </param>
    public void ClearLamp(int lamp)
    {
        Apply(lamp, TDVMessageLampState.Off);
    }

    /// <summary>
    /// Turns every lamp off. Used by a terminal reset.
    /// </summary>
    public void Clear()
    {
        Apply(0, TDVMessageLampState.Off);
    }

    /// <summary>
    /// Whether any lamp is lit or blinking.
    /// </summary>
    public bool AnyLampOn
    {
        get
        {
            for (int i = 0; i < LampCount; i++)
            {
                if (_lamps[i] != TDVMessageLampState.Off)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Sets one lamp, or all of them, and raises the event only on a real change.
    /// </summary>
    /// <param name="lamp">
    /// The lamp number, 1 to 4, or 0 for all.
    /// </param>
    /// <param name="state">
    /// The state to put it in.
    /// </param>
    /// <remarks>
    /// A parameter outside 0 to 4 is ignored. Section 5.52: "For undefined parameters, the sequence
    /// is ignored and a parameter error occurs." The error condition is not latched anywhere yet -
    /// see the NDRQ report type 5, which answers "nothing latched" for the same reason.
    /// </remarks>
    private void Apply(int lamp, TDVMessageLampState state)
    {
        if (lamp < 0 || lamp > LampCount)
        {
            return;
        }

        bool changed = false;

        if (lamp == 0)
        {
            for (int i = 0; i < LampCount; i++)
            {
                if (_lamps[i] != state)
                {
                    _lamps[i] = state;
                    changed = true;
                }
            }
        }
        else if (_lamps[lamp - 1] != state)
        {
            _lamps[lamp - 1] = state;
            changed = true;
        }

        if (changed)
        {
            StateChanged?.Invoke();
        }
    }
}

/// <summary>
/// What one message lamp is doing.
/// </summary>
/// <remarks>
/// Exclusive by design - see the remarks on <see cref="TDVMessageLEDs"/>.
/// </remarks>
public enum TDVMessageLampState
{
    /// <summary>
    /// Dark.
    /// </summary>
    Off,

    /// <summary>
    /// Lit steadily, by NDSLED.
    /// </summary>
    Lit,

    /// <summary>
    /// Blinking, by NDBLED.
    /// </summary>
    Blinking
}

/// <summary>
/// The four message lamps, by the names the manual gives them.
/// </summary>
/// <remarks>
/// ND Display Terminal 1200 section 5.52. The numbers are the parameter values of NDSLED, NDCLED
/// and NDBLED, so the enum is numbered to match and can be cast straight from a parameter.
/// </remarks>
public enum TDVMessageLamp
{
    /// <summary>
    /// Lamp 1.
    /// </summary>
    Expand = 1,

    /// <summary>
    /// Lamp 2.
    /// </summary>
    Append = 2,

    /// <summary>
    /// Lamp 3.
    /// </summary>
    Busy = 3,

    /// <summary>
    /// Lamp 4.
    /// </summary>
    Message = 4
}
