//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Text;

using RetroIO.Protocols.Tn3270;

namespace RetroTerm.Ibm3270
{
    /// <summary>
    /// Called when the host sent something the 3287 model rejects or has not built. The text says
    /// what and where the rule comes from.
    /// </summary>
    /// <param name="message">
    /// What was wrong.
    /// </param>
    public delegate void PrinterMessage(string message);

    /// <summary>
    /// An IBM 3287 printer on a non-SNA 3270 data stream: a character buffer that the host writes
    /// with the ordinary 3270 write commands, and that is printed when the Write Control Character
    /// asks for it. The printed lines collect into the current print job until the owner takes it
    /// with <see cref="TakeJob"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>The authority</b></para>
    /// GA23-0059-4 (3270 Data Stream Programmer's Reference, December 1988), chapter 8 "Printer
    /// Considerations", pages 8-1 and 8-2, Table 8-1 (the WCC as a printer reads it). Quoted where
    /// it is used below. x3270's pr3287 (Common\pr3287\ctlr.c) was read as a second opinion, never
    /// copied; where the two differ the manual is followed and the difference is written down at
    /// the place it matters.
    ///
    /// <para><b>When printing happens</b></para>
    /// Only when a write's WCC has the Start Print bit (X'08'): "When set to 1, initiates a printout
    /// operation at the completion of the write operation" (Table 8-1, bit 4). Data written without
    /// it stays in the buffer and is NOT printed. pr3287 instead prints the buffer when the next
    /// erase arrives or at end of job, whatever the WCC said; that is the one place this model and
    /// pr3287 can give a different result on the same data.
    ///
    /// <para><b>The buffer is not cleared by printing</b></para>
    /// The manual says what erases it (Erase/Write, Erase/Write Alternate, Erase All Unprotected)
    /// and does not list printing, so a second Start Print without an erase prints the same text
    /// again, as the paper would show it.
    ///
    /// <para><b>Buffer size</b></para>
    /// 3564 positions, 27 x 132: Hercules makes every IBM-3287-1 client a model 5 ("tn->model = '5';
    /// // (132 columns)", hyperion console.c, negotiate_ttype), and a model 5 is 27 by 132 (RFC 1576
    /// section 4). A real 3287's buffer size depends on its control unit; this is the size the one
    /// host we can test against uses.
    /// </remarks>
    public sealed class Printer3287
    {
        /// <summary>
        /// The buffer size: 27 rows of 132, a model 5 (see the class remarks).
        /// </summary>
        public const int BufferSize = 27 * 132;

        /// <summary>
        /// The longest line when the WCC gives no line length: "Provides a 132-character print line
        /// when orders are not present" (GA23-0059-4 Table 8-1, bits 2 and 3 = 00), and the printer
        /// starts a new line by itself at the end of the carriage (p. 8-2).
        /// </summary>
        public const int UnformattedLineLength = 132;

        // The channel (local, non-SNA) command codes. Hercules translates the channel program's
        // codes into the remote ones before sending (hyperion console.c, the L3270_ and R3270_
        // tables, lines 1233-1248 and the "write:" block), so TK5 sends the remote ones; these are
        // accepted too because pr3287 accepts both (ctlr.c process_ds, CMD_ and SNA_CMD_).
        private const byte LocalWrite = 0x01;
        private const byte LocalEraseWrite = 0x05;
        private const byte LocalEraseWriteAlternate = 0x0D;
        private const byte LocalEraseAllUnprotected = 0x0F;
        private const byte LocalWriteStructuredField = 0x11;
        private const byte LocalNoOperation = 0x03;
        private const byte LocalReadBuffer = 0x02;
        private const byte LocalReadModified = 0x06;
        private const byte RemoteReadBuffer = 0xF2;
        private const byte RemoteReadModified = 0xF6;
        private const byte RemoteReadModifiedAll = 0x6E;

        // Extended attribute type for the 3270 field attribute inside SFE and MF (GA23-0059-4 p. 4-7).
        private const byte AttributeTypeField = 0xC0;

        // Each buffer position: the EBCDIC byte (0 = null), whether it is a field attribute, and
        // whether its character came through Graphic Escape (the alternate character set).
        private readonly byte[] _cells = new byte[BufferSize];
        private readonly bool[] _isFieldAttribute = new bool[BufferSize];
        private readonly bool[] _isGraphicEscape = new bool[BufferSize];

        // The pages of the job being built. The last page is the one being printed on.
        private List<List<string>> _pages = new List<List<string>>();

        // True once a printout put anything into the current job (a line or a page eject).
        private bool _jobHasOutput;

        private long _jobBytes;

        private int _address;

        private CodePage _codePage = CodePage.Cp037;

        /// <summary>
        /// Creates an empty printer: buffer all nulls, no job.
        /// </summary>
        public Printer3287()
        {
            _pages.Add(new List<string>());
        }

        /// <summary>
        /// Raised when the host sent something this model rejects or has not built. Never
        /// swallowed: the owner shows it.
        /// </summary>
        public event PrinterMessage? ProtocolError;

        /// <summary>
        /// Gets a value indicating whether the current job has printed anything yet.
        /// </summary>
        public bool HasOutput => _jobHasOutput;

        /// <summary>
        /// Gets how many record bytes arrived since the last job was taken.
        /// </summary>
        public long PendingBytes => _jobBytes;

        /// <summary>
        /// Gets how many printouts (Start Print operations) have been done since the printer was made.
        /// </summary>
        public int PrintoutCount { get; private set; }

        /// <summary>
        /// Sets the EBCDIC code page used to turn the buffer into text. 037 until set.
        /// </summary>
        /// <param name="codePage">
        /// The code page.
        /// </param>
        public void SetCodePage(CodePage codePage)
        {
            ArgumentNullException.ThrowIfNull(codePage);
            _codePage = codePage;
        }

        /// <summary>
        /// Applies one complete 3270 record from the host: command byte first, no telnet framing.
        /// </summary>
        /// <param name="record">
        /// The record.
        /// </param>
        public void ProcessRecord(ReadOnlySpan<byte> record)
        {
            _jobBytes += record.Length;
            if (record.IsEmpty)
            {
                return;
            }

            byte command = record[0];
            if (command == Command3270.EraseWrite || command == LocalEraseWrite
                || command == Command3270.EraseWriteAlternate || command == LocalEraseWriteAlternate)
            {
                // Erase/Write: the buffer goes to nulls and the address to 0 before the write
                // (GA23-0059-4 p. 3-5). A printer has one buffer size, so the alternate form does
                // the same.
                EraseAll();
                Write(record);
                return;
            }

            if (command == Command3270.Write || command == LocalWrite)
            {
                Write(record);
                return;
            }

            if (command == Command3270.EraseAllUnprotected || command == LocalEraseAllUnprotected)
            {
                // No WCC follows EAU (p. 3-6). Every unprotected position goes to null.
                EraseUnprotected(0, BufferSize);
                _address = 0;
                return;
            }

            if (command == LocalNoOperation)
            {
                return;
            }

            if (command == Command3270.WriteStructuredField || command == LocalWriteStructuredField)
            {
                ProtocolError?.Invoke("Write Structured Field to the printer is not built (GA23-0059-4 chapter 8: printers accept a limited set of structured fields). The record was not printed.");
                return;
            }

            if (command == RemoteReadBuffer || command == RemoteReadModified || command == RemoteReadModifiedAll
                || command == LocalReadBuffer || command == LocalReadModified)
            {
                ProtocolError?.Invoke("The host asked the printer for a read (command X'" + command.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + "'); reading a 3287 buffer is not built.");
                return;
            }

            ProtocolError?.Invoke("Unknown 3270 command X'" + command.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + "' sent to the printer; the record was not printed.");
        }

        /// <summary>
        /// Hands over the job built so far and starts a new one. The buffer is kept: it belongs to
        /// the device, not to the job.
        /// </summary>
        /// <returns>
        /// The job, or null when nothing was printed since the last one.
        /// </returns>
        public PrintJob? TakeJob()
        {
            if (!_jobHasOutput)
            {
                _jobBytes = 0;
                return null;
            }

            // A Form Feed at the very end only ejects the last sheet. The page it starts has no ink
            // on it (at most the FF's own space and the final New Line), and it would only leave
            // the printer with the next job's first Form Feed, so it is not a page of this job.
            if (_pages.Count > 1 && !HasInk(_pages[_pages.Count - 1]))
            {
                _pages.RemoveAt(_pages.Count - 1);
            }

            var job = new PrintJob(_pages, _jobBytes);
            _pages = new List<List<string>>();
            _pages.Add(new List<string>());
            _jobHasOutput = false;
            _jobBytes = 0;
            return job;
        }

        private static bool HasInk(List<string> page)
        {
            for (int i = 0; i < page.Count; i++)
            {
                if (page[i].Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void EraseAll()
        {
            Array.Clear(_cells);
            Array.Clear(_isFieldAttribute);
            Array.Clear(_isGraphicEscape);
            _address = 0;
        }

        // W, EW, EWA after the command byte: the WCC, then orders and data (GA23-0059-4 p. 3-3).
        private void Write(ReadOnlySpan<byte> record)
        {
            // "A write command with no WCC and no data is a no-op" (p. 3-2).
            if (record.Length < 2)
            {
                return;
            }

            byte wcc = record[1];

            // Bits 2 and 3 (X'30') give the print format (Table 8-1): 00 = the NL and EM orders
            // decide, 01 = 40, 10 = 64, 11 = 80 characters a line.
            int lineLength = ((wcc >> 4) & 0x03) switch
            {
                1 => 40,
                2 => 64,
                3 => 80,
                _ => 0,
            };

            int i = 2;
            while (i < record.Length)
            {
                byte b = record[i];
                if (b == Order3270.StartField)
                {
                    if (i + 1 >= record.Length)
                    {
                        CutShort("Start Field");
                        return;
                    }

                    PutFieldAttribute(record[i + 1]);
                    i += 2;
                }
                else if (b == Order3270.SetBufferAddress)
                {
                    if (!TryReadAddress(record, i + 1, "Set Buffer Address", out int target))
                    {
                        return;
                    }

                    _address = target;
                    i += 3;
                }
                else if (b == Order3270.InsertCursor)
                {
                    // A printer has no cursor to show; the order changes nothing that prints.
                    i++;
                }
                else if (b == Order3270.ProgramTab)
                {
                    ProgramTab();
                    i++;
                }
                else if (b == Order3270.RepeatToAddress)
                {
                    if (!TryReadAddress(record, i + 1, "Repeat to Address", out int target))
                    {
                        return;
                    }

                    int c = i + 3;
                    bool graphicEscape = false;
                    if (c < record.Length && record[c] == Order3270.GraphicEscape)
                    {
                        graphicEscape = true;
                        c++;
                    }

                    if (c >= record.Length)
                    {
                        CutShort("Repeat to Address");
                        return;
                    }

                    // "If the stop address equals the current address, the whole buffer is
                    // filled" (GA23-0059-4 p. 4-8): the do-while writes at least one position and
                    // stops when it comes round to the target.
                    do
                    {
                        PutCharacter(record[c], graphicEscape);
                    }
                    while (_address != target);

                    i = c + 1;
                }
                else if (b == Order3270.EraseUnprotectedToAddress)
                {
                    if (!TryReadAddress(record, i + 1, "Erase Unprotected to Address", out int target))
                    {
                        return;
                    }

                    int count = target > _address ? target - _address : BufferSize - _address + target;
                    EraseUnprotected(_address, count);
                    _address = target;
                    i += 3;
                }
                else if (b == Order3270.GraphicEscape)
                {
                    if (i + 1 >= record.Length)
                    {
                        CutShort("Graphic Escape");
                        return;
                    }

                    PutCharacter(record[i + 1], true);
                    i += 2;
                }
                else if (b == Order3270.StartFieldExtended || b == Order3270.ModifyField)
                {
                    if (i + 1 >= record.Length || i + 2 + (record[i + 1] * 2) > record.Length)
                    {
                        CutShort(b == Order3270.ModifyField ? "Modify Field" : "Start Field Extended");
                        return;
                    }

                    int pairs = record[i + 1];
                    byte attribute = 0;
                    bool hasFieldAttribute = false;
                    for (int p = 0; p < pairs; p++)
                    {
                        // Only the 3270 field attribute matters on paper here; colour and
                        // highlighting are not printed by this model (it makes black text).
                        if (record[i + 2 + (p * 2)] == AttributeTypeField)
                        {
                            attribute = record[i + 3 + (p * 2)];
                            hasFieldAttribute = true;
                        }
                    }

                    if (b == Order3270.StartFieldExtended)
                    {
                        PutFieldAttribute(attribute);
                    }
                    else
                    {
                        // Modify Field changes the attribute at the current address, if there is
                        // one there, and steps one position on (GA23-0059-4 p. 4-9).
                        if (hasFieldAttribute && _isFieldAttribute[_address])
                        {
                            _cells[_address] = attribute;
                        }

                        _address = (_address + 1) % BufferSize;
                    }

                    i += 2 + (pairs * 2);
                }
                else if (b == Order3270.SetAttribute)
                {
                    // Character colour or highlighting: nothing this black-ink model prints.
                    if (i + 2 >= record.Length)
                    {
                        CutShort("Set Attribute");
                        return;
                    }

                    i += 3;
                }
                else if (b >= 0x40 || IsStoredControl(b))
                {
                    PutCharacter(b, false);
                    i++;
                }
                else
                {
                    // X'00'-X'3F' that is neither an order nor a stored control code is rejected
                    // by a real 3270 (sense X'1003', p. 4-1). Said, then stored as a null so the
                    // address stays where the host expects it.
                    ProtocolError?.Invoke("Byte X'" + b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + "' at offset " + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is not an order or a printable character (GA23-0059-4 p. 4-1); printed as nothing.");
                    PutCharacter(FormatControl.Null, false);
                    i++;
                }
            }

            if ((wcc & Wcc.StartPrinter) != 0)
            {
                Printout(lineLength);
            }
        }

        // NUL, FF, CR, NL, EM, DUP, FM and SUB go into the buffer like characters (p. 4-10).
        private static bool IsStoredControl(byte b) =>
            b == FormatControl.Null || b == FormatControl.FormFeed || b == FormatControl.CarriageReturn
            || b == FormatControl.NewLine || b == FormatControl.EndOfMedium || b == FormatControl.Dup
            || b == FormatControl.FieldMark || b == FormatControl.Substitute;

        private void PutCharacter(byte b, bool graphicEscape)
        {
            _cells[_address] = b;
            _isFieldAttribute[_address] = false;
            _isGraphicEscape[_address] = graphicEscape;
            _address = (_address + 1) % BufferSize;
        }

        private void PutFieldAttribute(byte attribute)
        {
            _cells[_address] = attribute;
            _isFieldAttribute[_address] = true;
            _isGraphicEscape[_address] = false;
            _address = (_address + 1) % BufferSize;
        }

        // Program Tab: to the first position of the next unprotected field (GA23-0059-4 p. 4-7);
        // with no unprotected field after the current address, to address 0.
        private void ProgramTab()
        {
            for (int n = 0; n < BufferSize; n++)
            {
                int a = (_address + n) % BufferSize;
                if (_isFieldAttribute[a] && !FieldAttribute.IsProtected(_cells[a]))
                {
                    _address = (a + 1) % BufferSize;
                    return;
                }

                if (a == BufferSize - 1)
                {
                    break;
                }
            }

            _address = 0;
        }

        private void EraseUnprotected(int from, int count)
        {
            bool isProtected = FieldIsProtectedAt(from);
            for (int n = 0; n < count; n++)
            {
                int a = (from + n) % BufferSize;
                if (_isFieldAttribute[a])
                {
                    isProtected = FieldAttribute.IsProtected(_cells[a]);
                    continue;
                }

                if (!isProtected)
                {
                    _cells[a] = FormatControl.Null;
                    _isGraphicEscape[a] = false;
                }
            }
        }

        // The field that holds an address: the nearest field attribute before it, going round the
        // end of the buffer. No field attribute at all means an unformatted buffer: unprotected.
        private bool FieldIsProtectedAt(int address)
        {
            int fa = FieldAttributeFor(address);
            return fa >= 0 && FieldAttribute.IsProtected(_cells[fa]);
        }

        private int FieldAttributeFor(int address)
        {
            for (int n = 0; n < BufferSize; n++)
            {
                int a = (address - n + BufferSize) % BufferSize;
                if (_isFieldAttribute[a])
                {
                    return a;
                }
            }

            return -1;
        }

        private bool TryReadAddress(ReadOnlySpan<byte> record, int at, string order, out int address)
        {
            address = 0;
            if (at + 1 >= record.Length)
            {
                CutShort(order);
                return false;
            }

            if (!BufferAddress.TryDecode(record[at], record[at + 1], out address) || address >= BufferSize)
            {
                ProtocolError?.Invoke(order + " names a buffer address outside the printer's " + BufferSize.ToString(System.Globalization.CultureInfo.InvariantCulture) + " positions; the rest of the write was not done (GA23-0059-4 p. 4-3).");
                return false;
            }

            return true;
        }

        private void CutShort(string order)
        {
            // "If a control sequence attempts to span a chain, it is ended on the chain boundary"
            // (GA23-0059-4 p. 4-1).
            ProtocolError?.Invoke("The record ended in the middle of a " + order + " order; the rest of the write was not done.");
        }

        private List<string> CurrentPage => _pages[_pages.Count - 1];

        private void NewPage()
        {
            _pages.Add(new List<string>());
            _jobHasOutput = true;
        }

        private void AddLine(string line)
        {
            CurrentPage.Add(line.TrimEnd(' '));
            _jobHasOutput = true;
        }

        // One printout of the whole buffer (GA23-0059-4 pp. 8-1, 8-2).
        private void Printout(int lineLength)
        {
            PrintoutCount++;
            if (lineLength == 0)
            {
                PrintUnformatted();
            }
            else
            {
                PrintFormatted(lineLength);
            }
        }

        // The character a buffer position prints as, or '\0' for "nothing here" (a null).
        private char Printable(int address)
        {
            byte b = _cells[address];
            if (b == FormatControl.Dup)
            {
                // Shown on a display as an overscore asterisk, a semicolon for FM (p. 4-10). A
                // text file has neither; pr3287 prints '*' and ';' (its ebc2asc0 table).
                return '*';
            }

            if (b == FormatControl.FieldMark)
            {
                return ';';
            }

            if (b < 0x40 || b == 0xFF)
            {
                // NUL, SUB, EO and the print controls never print as a character of their own.
                return '\0';
            }

            char c = _isGraphicEscape[address] ? CodePage.Cp310.ToUnicode(b) : _codePage.ToUnicode(b);
            return c;
        }

        // Line-length format (WCC bits 2-3 not 00): the buffer is cut into lines of that length.
        // "If a line contains only null characters and one or more space characters, a line feed
        // is performed to cause a blank line." Nulls, field attributes and characters in a
        // nonprint field are spaces in a line that has other print data; "if they constitute an
        // entire line, they are ignored, and the line feed is not performed". NL, CR and EM are
        // only valid without a line-length format and otherwise print as spaces; "FF is valid in
        // any buffer position", and its own position prints as a space (p. 8-2).
        private void PrintFormatted(int lineLength)
        {
            int fa = FieldAttributeFor(BufferSize - 1);
            bool printable = fa < 0 || !IsNonDisplay(_cells[fa]);

            var line = new StringBuilder(lineLength);
            bool lineHasData = false;
            for (int address = 0; address < BufferSize; address++)
            {
                if (address % lineLength == 0 && address > 0)
                {
                    if (lineHasData)
                    {
                        AddLine(line.ToString());
                    }

                    line.Clear();
                    lineHasData = false;
                }

                if (_isFieldAttribute[address])
                {
                    printable = !IsNonDisplay(_cells[address]);
                    line.Append(' ');
                    continue;
                }

                byte b = _cells[address];
                if (b == FormatControl.FormFeed)
                {
                    if (lineHasData)
                    {
                        AddLine(line.ToString());
                    }

                    NewPage();
                    int column = address % lineLength;
                    line.Clear();
                    line.Append(' ', column + 1);
                    lineHasData = false;
                    continue;
                }

                char c = printable ? Printable(address) : '\0';
                if (c == '\0')
                {
                    line.Append(' ');
                    continue;
                }

                line.Append(c);
                lineHasData = true;
            }

            if (lineHasData)
            {
                AddLine(line.ToString());
            }
        }

        // No line-length format: "printout of the buffer begins at buffer address 0 and continues
        // until the last position of the character buffer is printed or until a valid EM
        // character is encountered. ... At the end of each printout, a final New Line is executed
        // ... When the print-terminating EM order appears in the first print position of the print
        // line, a final New Line is not executed" (p. 8-2). NL, CR and EM count only inside a print
        // field; elsewhere they print as spaces. Past 132 characters the printer starts a new line
        // by itself.
        //
        // Nulls take no print position here. The manual describes nulls only for the line-length
        // format; pr3287 skips them in this mode (ctlr.c dump_unformatted, case '\0'), and so does
        // this. NOT VERIFIED against a real 3287.
        //
        // CR returns to the left margin and the next characters print over the line. On paper both
        // would show; in text, a later non-space character replaces the earlier one and a space
        // leaves it (pr3287 does the same, uoutput: "Don't overwrite with spaces").
        private void PrintUnformatted()
        {
            int fa = FieldAttributeFor(BufferSize - 1);
            bool printable = fa < 0 || !IsNonDisplay(_cells[fa]);

            char[] line = new char[UnformattedLineLength];
            int length = 0;
            int column = 0;
            bool endedByEm = false;
            for (int address = 0; address < BufferSize && !endedByEm; address++)
            {
                char c;
                if (_isFieldAttribute[address])
                {
                    printable = !IsNonDisplay(_cells[address]);
                    c = ' ';
                }
                else
                {
                    byte b = _cells[address];
                    if (b == FormatControl.Null)
                    {
                        continue;
                    }

                    if (b == FormatControl.FormFeed)
                    {
                        // Valid anywhere: the sheet goes and the FF's own position is a space at
                        // the left margin of the new page.
                        if (length > 0)
                        {
                            AddLine(new string(line, 0, length));
                        }

                        NewPage();
                        line[0] = ' ';
                        length = 1;
                        column = 1;
                        continue;
                    }

                    if (printable && b == FormatControl.NewLine)
                    {
                        AddLine(new string(line, 0, length));
                        length = 0;
                        column = 0;
                        continue;
                    }

                    if (printable && b == FormatControl.CarriageReturn)
                    {
                        column = 0;
                        continue;
                    }

                    if (printable && b == FormatControl.EndOfMedium)
                    {
                        endedByEm = true;
                        break;
                    }

                    c = printable ? Printable(address) : ' ';
                    if (c == '\0')
                    {
                        c = ' ';
                    }
                }

                if (column >= UnformattedLineLength)
                {
                    AddLine(new string(line, 0, length));
                    length = 0;
                    column = 0;
                }

                if (column >= length)
                {
                    // Positions skipped by nothing are blank.
                    for (int k = length; k < column; k++)
                    {
                        line[k] = ' ';
                    }

                    line[column] = c;
                    length = column + 1;
                }
                else if (c != ' ')
                {
                    line[column] = c;
                }

                column++;
            }

            // The final New Line, unless EM stopped the printout at the left margin. Without an EM
            // it is done even at the left margin, which leaves an empty line (p. 8-2). A line that
            // was printed and then returned to the margin by CR is still on the paper, so its text
            // is kept even when EM then stops at the margin.
            if (!endedByEm || length > 0)
            {
                AddLine(new string(line, 0, length));
            }
        }

        private static bool IsNonDisplay(byte attribute) =>
            (attribute & FieldAttribute.DisplayMask) == FieldAttribute.NonDisplay;
    }
}
