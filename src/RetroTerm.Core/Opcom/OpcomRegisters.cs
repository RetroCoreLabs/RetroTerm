using System;

namespace RetroTerm.Core.Opcom;

/// <summary>
/// Names of the 8 working registers in each program level.
/// </summary>
public static class WorkingRegisterNames
{
    public const int Count = 8;

    /// <summary>
    /// Register names indexed by register number (0-7).
    /// </summary>
    public static readonly string[] ByNumber = { "S", "D", "P", "B", "L", "A", "T", "X" };

    /// <summary>
    /// Full names indexed by register number.
    /// </summary>
    public static readonly string[] FullNames = { "Status", "D", "Program Counter", "Base", "Link", "Accumulator", "T", "Index" };
}

/// <summary>
/// Internal register definitions (I0-I15, octal I0-I17).
/// These map to the TRA instruction codes for reading.
/// </summary>
public static class InternalRegisterDefs
{
    public const int Count = 14; // I0 through I15 (octal I0-I15)

    /// <summary>
    /// How many values a real IRD dump prints. Measured on the ND-110 over COM11 on
    /// 6 September 2026: fifteen, one more than this table names and one fewer than the
    /// manual promises. The dump parser counts to this number because the machine sends
    /// nothing after the last value.
    /// </summary>
    public const int DumpWordCount = 15;

    /// <summary>
    /// Register number, OPCOM name, full name, and whether it's writable from OPCOM.
    /// </summary>
    public static readonly (int Number, string OpcomName, string FullName, bool Writable)[] Registers =
    {
        (0,  "PANS", "Panel Status",              false),
        (1,  "STS",  "Status",                    false),
        (2,  "OPR",  "Operator Panel Register",   true),   // Writable via OPR/ + value DEP
        (3,  "PSR",  "Program Status Register",   false),
        (4,  "PVL",  "Previous Level",            false),
        (5,  "IIC",  "Internal Interrupt Code",   false),
        (6,  "PID",  "Program Identification",    false),
        (7,  "PIE",  "Priority Interrupt Enable", false),
        (8,  "CSR",  "Cache Status Register",     false),  // Octal I10
        (9,  "ACTL", "Active Level",              false),  // Octal I11
        (10, "ALD",  "Automatic Load Descriptor", true),   // Octal I12, writable
        (11, "PES",  "Paging Error Status",       false),  // Octal I13
        (12, "PCR",  "Program Control Register",  false),  // Octal I14
        (13, "PEA",  "Paging Error Address",      false),  // Octal I15
    };

    /// <summary>
    /// Gets the OPCOM command string for reading an internal register by its array index.
    /// OPCOM uses Iy notation where y is the octal register number (I0-I7, I10-I15).
    /// Special case: OPR can also be addressed by name.
    /// </summary>
    public static string GetReadCommand(int index)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        var reg = Registers[index];
        // Use "I" + octal number notation (the only valid OPCOM syntax for internal registers)
        // OPR (I2) can also use "OPR" but Ixx works for all.
        int octalNum = reg.Number;
        if (octalNum < 8)
            return "I" + octalNum.ToString();
        else
            return "I" + OctalHelper.ToOctalTrimmed(octalNum);
    }
}

/// <summary>
/// Holds the register state for a single program level (0-15).
/// </summary>
public struct WorkingRegisterSet
{
    public ushort S;   // R0 - Status
    public ushort D;   // R1 - D register
    public ushort P;   // R2 - Program counter
    public ushort B;   // R3 - Base
    public ushort L;   // R4 - Link
    public ushort A;   // R5 - Accumulator
    public ushort T;   // R6 - T register
    public ushort X;   // R7 - Index

    public ushort GetByIndex(int index)
    {
        return index switch
        {
            0 => S,
            1 => D,
            2 => P,
            3 => B,
            4 => L,
            5 => A,
            6 => T,
            7 => X,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    public void SetByIndex(int index, ushort value)
    {
        switch (index)
        {
            case 0: S = value; break;
            case 1: D = value; break;
            case 2: P = value; break;
            case 3: B = value; break;
            case 4: L = value; break;
            case 5: A = value; break;
            case 6: T = value; break;
            case 7: X = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}

/// <summary>
/// Complete register state model for the ND-100 CPU as seen via OPCOM.
/// 16 program levels x 8 working registers + 14 internal registers.
/// </summary>
public sealed class OpcomRegisterState
{
    public const int MaxLevels = 16; // 0-17 octal

    /// <summary>
    /// Working registers for each of the 16 program levels.
    /// </summary>
    public readonly WorkingRegisterSet[] Levels = new WorkingRegisterSet[MaxLevels];

    /// <summary>
    /// Internal register values indexed by InternalRegisterDefs array index.
    /// </summary>
    public readonly ushort[] Internal = new ushort[InternalRegisterDefs.Count];

    /// <summary>
    /// Tracks which levels have been read (bitfield).
    /// </summary>
    public int LevelsRead;

    /// <summary>
    /// Whether internal registers have been read.
    /// </summary>
    public bool InternalRead;

    /// <summary>
    /// Event raised when any register value changes.
    /// </summary>
    public event Action? RegistersChanged;

    public void SetWorkingRegister(int level, int regIndex, ushort value)
    {
        if (level < 0 || level >= MaxLevels) return;
        Levels[level].SetByIndex(regIndex, value);
        LevelsRead |= (1 << level);
        RegistersChanged?.Invoke();
    }

    public void SetInternalRegister(int index, ushort value)
    {
        if (index < 0 || index >= InternalRegisterDefs.Count) return;
        Internal[index] = value;
        InternalRead = true;
        RegistersChanged?.Invoke();
    }

    public void Clear()
    {
        for (int i = 0; i < MaxLevels; i++)
            Levels[i] = default;
        for (int i = 0; i < InternalRegisterDefs.Count; i++)
            Internal[i] = 0;
        LevelsRead = 0;
        InternalRead = false;
        RegistersChanged?.Invoke();
    }
}
