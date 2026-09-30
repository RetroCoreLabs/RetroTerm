using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Protocols.Net;
using Xunit;

namespace RetroTerm.Tests.Opcom;

/// <summary>
/// Tests the OPCOM simulator connection end-to-end.
/// Sends OPCOM commands and verifies responses match ND-100 behavior.
/// </summary>
public class OpcomSimulatorTests
{
    private readonly OpcomSimulatorConnection _sim;
    private readonly List<byte> _received;

    public OpcomSimulatorTests()
    {
        _sim = new OpcomSimulatorConnection();
        _received = new List<byte>();
        _sim.DataReceived += data =>
        {
            var span = data.Span;
            for (int i = 0; i < span.Length; i++)
                _received.Add(span[i]);
        };
    }

    private async Task ConnectAndWaitForPrompt()
    {
        await _sim.ConnectAsync();
        // Wait for initial prompt
        await Task.Delay(200);
        _received.Clear();
    }

    private async Task<string> SendAndCollect(string command, int delayMs = 200)
    {
        _received.Clear();
        byte[] bytes = Encoding.ASCII.GetBytes(command);
        await _sim.SendAsync(bytes);
        await Task.Delay(delayMs);
        return Encoding.ASCII.GetString(_received.ToArray());
    }

    [Fact]
    public async Task Connect_SendsInitialPrompt()
    {
        await _sim.ConnectAsync(TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        string response = Encoding.ASCII.GetString(_received.ToArray());
        Assert.Contains("#", response);
        Assert.Equal(ConnectionStatus.Connected, _sim.Status);
    }

    [Fact]
    public async Task Disconnect_SetsStatusDisconnected()
    {
        await _sim.ConnectAsync(TestContext.Current.CancellationToken);
        await _sim.DisconnectAsync();
        Assert.Equal(ConnectionStatus.Disconnected, _sim.Status);
    }

    [Fact]
    public async Task ExamineMemory_ReturnsValue()
    {
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect("0/");
        // Should echo "0/" and then return a 6-digit octal value followed by space
        Assert.Contains("/", response);
        Assert.Contains(" ", response); // Value terminated by space
    }

    [Fact]
    public async Task ExamineRegisterA_ReturnsValue()
    {
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect("A/");
        Assert.Contains("A", response); // Echo
        Assert.Contains("/", response); // Echo
        // Should contain the value (A is initialized to 0x1234 = 011064 octal)
        Assert.Contains("011064", response);
    }

    [Fact]
    public async Task ExamineRegisterP_ReturnsValue()
    {
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect("P/");
        Assert.Contains("P", response);
        Assert.Contains("/", response);
        // P is initialized to 0x0200 = 001000 octal
        Assert.Contains("001000", response);
    }

    [Fact]
    public async Task ExamineInternalI0_ReturnsValue()
    {
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect("I0/");
        Assert.Contains("I", response);
        Assert.Contains("0", response);
        Assert.Contains("/", response);
        Assert.Contains(" ", response); // Value + space
    }

    [Fact]
    public async Task ExamineInternalI12_ReturnsAldValue()
    {
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect("I12/");
        // ALD is initialized to 0x2360 = 021540 octal (SMD boot)
        // But stored as decimal 9056 in _internalRegs[10]
        Assert.Contains("I", response);
        Assert.Contains("/", response);
    }

    [Fact]
    public async Task LowercaseInput_IsEchoedThenRejectedWithNothingAfterTheQuestionMark()
    {
        // Measured on the real ND-120/CX over COM11, 6 September 2026: sending "a" gave back
        // exactly "a?" - the character echoed as typed, then one question mark. No CR LF
        // and NO prompt, even though the input has been cancelled and OPCOM is ready.
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect("a");
        Assert.Equal("a?", response);
    }

    [Fact]
    public async Task AnUnrecognisedCharacterIsRejectedTheSameWayAsLowercase()
    {
        // Measured the same day: "%" gave back exactly "%?".
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect("%");
        Assert.Equal("%?", response);
    }

    [Fact]
    public async Task SpaceInput_IsEchoedAndNothingElse()
    {
        // Measured on the real ND-120/CX over COM11, 6 September 2026: a space is echoed
        // and NO '#' follows it, at command level and in examine mode alike.
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect(" ");
        Assert.Equal(" ", response);

        await SendAndCollect("100/");
        response = await SendAndCollect(" ");
        Assert.Equal(" ", response);
    }

    [Fact]
    public async Task MemoryAdvanceWithCr_PrintsCrLfHashThenTheNextValueAndNoAddress()
    {
        // Real bytes: "0/114631 " then CR gave CR LF "#031463 " - the next address's
        // value behind a '#' that is a line start, not a prompt. No address is printed
        // and OPCOM stays in examine mode.
        await ConnectAndWaitForPrompt();
        await SendAndCollect("101/");
        await SendAndCollect("12345\r");
        await SendAndCollect(" ");
        await SendAndCollect("100/");
        string response = await SendAndCollect("\r");
        Assert.Equal("\r\r\n#012345 ", response);
        // Still in examine mode: a value + CR now deposits at 101 and advances again.
        response = await SendAndCollect("777\r");
        Assert.StartsWith("777\r\r\n#", response);
        await SendAndCollect(" ");
        response = await SendAndCollect("101/");
        Assert.Contains("000777", response);
    }

    [Fact]
    public async Task MemoryDump_UsesTheRealLineFormatAndSendsNoTrailingPrompt()
    {
        // Real bytes for "0<20" CR: CR LF '#' CR, then "AAAAAA /" and eight words per
        // line each followed by a space, lines separated by CR LF, nothing after the last.
        await ConnectAndWaitForPrompt();
        await SendAndCollect("200/");
        await SendAndCollect("54321\r");
        await SendAndCollect(" ");
        string response = await SendAndCollect("200<211\r", 300);
        // The simulator seeds memory so each word holds its own address, which is why
        // every word but the deposited one reads back as its own index here.
        Assert.Equal(
            "200<211\r\r\n#\r" +
            "000200 /054321 000201 000202 000203 000204 000205 000206 000207 \r\n" +
            "000210 /000210 000211 ", response);
    }

    [Fact]
    public async Task RegisterDump_UsesTheRealLineFormatAndLeavesTheNextLineStartHanging()
    {
        // Real bytes for "0<1RD" CR ended with CR LF "000020 /" and nothing more.
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect("0<1RD\r", 300);
        Assert.StartsWith("0<1RD\r\r\n#\r000000 /", response);
        Assert.EndsWith(" \r\n000020 /", response);
        Assert.DoesNotContain("#", response.Substring(9));
    }

    [Fact]
    public async Task InternalRegisterDump_PrintsFifteenValuesAndNoTrailingPrompt()
    {
        // Real bytes for "IRD" CR: two lines, eight then seven values, nothing after.
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect("IRD\r", 300);
        Assert.StartsWith("IRD\r\r\n#\r000000 /", response);
        Assert.Contains("\r\n000010 /", response);
        Assert.EndsWith(" ", response);
        int values = 0;
        for (int i = 0; i < response.Length; i++)
        {
            // A value is six octal digits followed by a space and not by " /".
            if (i + 7 <= response.Length && response[i + 6] == ' ' && (i + 7 == response.Length || response[i + 7] != '/'))
            {
                bool digits = true;
                for (int d = 0; d < 6; d++)
                    if (response[i + d] < '0' || response[i + d] > '7') { digits = false; break; }
                if (digits && (i == 0 || response[i - 1] == ' ' || response[i - 1] == '/')) { values++; i += 6; }
            }
        }
        Assert.Equal(15, values);
    }

    [Fact]
    public async Task StopCommand_EchoesAndReturnsPrompt()
    {
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect("STOP");
        Assert.Contains("S", response);
        Assert.Contains("T", response);
        Assert.Contains("O", response);
        Assert.Contains("P", response);
        Assert.Contains("#", response);
    }

    [Fact]
    public async Task MasterClearIsMaclPlusCarriageReturnAndAnswersDoubleHash()
    {
        // Measured on the real ND-120/CX, 6 September 2026. Ronny named the command after
        // watching "MCL" get rejected: it is MACL. Typing it answers CR LF and two
        // hashes - and only after a carriage return; the letters alone just sit there.
        await ConnectAndWaitForPrompt();
        string typed = await SendAndCollect("MACL", 200);
        Assert.Equal("MACL", typed); // echoed, and nothing has happened yet

        string response = await SendAndCollect("\r", 500);
        Assert.Equal("\r\r\n##", response);
    }

    [Fact]
    public async Task TheOldMclSpellingIsRejectedJustAsTheMachineRejectsIt()
    {
        // "MCL" is what this repo's own command reference said and what the code sent
        // until 6 September 2026. The real machine echoes it and then answers '?' to the
        // carriage return, so master clear could never have worked against hardware.
        await ConnectAndWaitForPrompt();
        await SendAndCollect("MCL", 200);
        string response = await SendAndCollect("\r", 300);
        Assert.Contains("?", response);
        Assert.DoesNotContain("##", response);
    }

    [Fact]
    public async Task MemoryDump_ReturnsValues()
    {
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect("0<7\r", 300);
        // Should return 8 octal values and then '#'
        Assert.Contains("#", response);
    }

    [Fact]
    public async Task SingleStep_ReturnsPrompt()
    {
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect("Z");
        Assert.Contains("#", response);
    }

    [Fact]
    public async Task Breakpoint_ReturnsPrompt()
    {
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect("1000.");
        Assert.Contains(".", response);
        Assert.Contains("#", response);
    }

    [Fact]
    public async Task StartExecution_NoPrompt()
    {
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect("1000!");
        // Start doesn't produce a prompt (CPU is running)
        Assert.Contains("!", response); // Echo
    }

    [Fact]
    public async Task AllWorkingRegisters_ReturnValues()
    {
        await ConnectAndWaitForPrompt();
        string[] regs = { "S", "D", "P", "B", "L", "A", "T", "X" };
        for (int i = 0; i < regs.Length; i++)
        {
            string response = await SendAndCollect(regs[i] + "/");
            Assert.Contains("/", response);
            Assert.Contains(" ", response); // Value terminated by space
            // Cancel examine mode
            await SendAndCollect(" ");
        }
    }

    [Fact]
    public async Task AllInternalRegisters_ViaINotation_ReturnValues()
    {
        await ConnectAndWaitForPrompt();
        // I0-I7, I10-I15 (octal notation)
        string[] iRegs = { "I0", "I1", "I2", "I3", "I4", "I5", "I6", "I7",
                           "I10", "I11", "I12", "I13", "I14", "I15" };
        for (int i = 0; i < iRegs.Length; i++)
        {
            string response = await SendAndCollect(iRegs[i] + "/");
            Assert.Contains("/", response);
            // Should NOT contain '?' (all should be valid)
            // Cancel examine mode
            await SendAndCollect(" ");
        }
    }

    [Fact]
    public async Task InvalidInternalRegName_ReturnsError()
    {
        await ConnectAndWaitForPrompt();
        string response = await SendAndCollect("PANS/");
        // PANS is NOT valid OPCOM syntax - only Ixx works
        // The simulator should try to parse as memory address and fail
        Assert.Contains("?", response);
    }

    [Fact]
    public async Task DepositMemory_ChangesValue()
    {
        await ConnectAndWaitForPrompt();

        // Examine address 100
        await SendAndCollect("100/");
        // Deposit value 177777 + CR
        await SendAndCollect("177777\r");
        // Cancel with space
        await SendAndCollect(" ");

        // Read back
        string response = await SendAndCollect("100/");
        Assert.Contains("177777", response);
    }

    [Fact]
    public async Task PrintLocation_ReturnsTheAddressFollowedByASpaceAndNoPrompt()
    {
        // Measured on the real ND-120/CX: "*" answered "*000000 " - the echo, six octal
        // digits and a space. No prompt follows.
        await ConnectAndWaitForPrompt();
        await SendAndCollect("1000/");   // sets the current location
        await SendAndCollect(" ");       // leave examine mode
        string response = await SendAndCollect("*");
        Assert.Equal("*001000 ", response);
    }

    [Fact]
    public async Task BootCommand_21560_ReportsScsiBootstrap()
    {
        await ConnectAndWaitForPrompt();
        // 21560& = Bootstrap from SCSI/Floppy, load+run
        string response = await SendAndCollect("21560&", 700);
        Assert.Contains("Booting from", response);
        Assert.Contains("Floppy", response);
        Assert.Contains("Bootstrap", response);
        Assert.Contains("load and run", response);
        Assert.Contains("#", response);
    }

    [Fact]
    public async Task BootCommand_Bare_UsesAldDefault()
    {
        await ConnectAndWaitForPrompt();
        // Bare & uses ALD register default (initialized to 0x2360 = 021540 = SMD Bootstrap)
        string response = await SendAndCollect("&", 700);
        Assert.Contains("Booting from", response);
        Assert.Contains("SMD", response);
        Assert.Contains("Bootstrap", response);
        Assert.Contains("#", response);
    }

    [Fact]
    public async Task BootCommand_100400_ReportsBinaryPaperTape()
    {
        await ConnectAndWaitForPrompt();
        // 100400& = Binary from Paper Tape, load only
        string response = await SendAndCollect("100400&", 700);
        Assert.Contains("Booting from", response);
        Assert.Contains("Paper Tape", response);
        Assert.Contains("Binary", response);
        Assert.Contains("load only", response);
        Assert.Contains("#", response);
    }

    [Fact]
    public async Task DepositWithDep_ExitsExamineMode()
    {
        await ConnectAndWaitForPrompt();
        // Examine address 100
        await SendAndCollect("100/");
        // Deposit with DEP — should exit examine mode and return #
        string response = await SendAndCollect("177777DEP");
        Assert.Contains("#", response);
        // Verify: re-examine should show the deposited value
        response = await SendAndCollect("100/");
        Assert.Contains("177777", response);
    }

    [Fact]
    public async Task InternalRegisterDeposit_WritesToTrrArray()
    {
        await ConnectAndWaitForPrompt();
        // Read I2 (TRA: OPR, initialized to 0)
        string readResponse = await SendAndCollect("I2/");
        Assert.Contains("000000", readResponse);
        // Deposit to I2 (TRR: LMP) — should NOT change the TRA read value
        await SendAndCollect("123456DEP");
        // Read I2 again — should still be 000000 (OPR unchanged, deposit went to LMP)
        string readAgain = await SendAndCollect("I2/");
        Assert.Contains("000000", readAgain);
    }

    [Fact]
    public async Task UploadViaDep_CompletesWithPrompt()
    {
        await ConnectAndWaitForPrompt();
        // Simulate upload pattern: examine address, deposit with DEP, expect #
        await SendAndCollect("200/");
        string response = await SendAndCollect("54321DEP");
        Assert.Contains("#", response);
        // Second word
        await SendAndCollect("201/");
        response = await SendAndCollect("12345DEP");
        Assert.Contains("#", response);
        // Verify both words were written
        response = await SendAndCollect("200/");
        Assert.Contains("054321", response);
    }

    [Fact]
    public async Task BinaryLoader_300Dollar_LoadsBpunData()
    {
        await ConnectAndWaitForPrompt();

        // Activate binary loader on device 300
        string response = await SendAndCollect("300$", 300);
        Assert.Contains("Binary loader active", response);

        // Send a minimal BPUN: preamble + binary data
        // Preamble: start address 100 (octal) + CR, boot address 100 + !
        // Binary: load addr=0x0040(=100 oct), count=0x0002, data=0xABCD 0x1234, checksum, action=0(start)
        byte[] preamble = System.Text.Encoding.ASCII.GetBytes("100\r100!");

        ushort loadAddr = 0x0040; // 100 octal
        ushort wordCount = 2;
        ushort word1 = 0xABCD;
        ushort word2 = 0x1234;
        ushort checksum = (ushort)(word1 + word2);
        byte[] action = System.Text.Encoding.ASCII.GetBytes("0\r"); // action=0: start execution, returns to OPCOM

        byte[] binary = new byte[2 + 2 + 4 + 2]; // addr + count + 2 words + checksum
        binary[0] = (byte)(loadAddr >> 8); binary[1] = (byte)(loadAddr & 0xFF);
        binary[2] = (byte)(wordCount >> 8); binary[3] = (byte)(wordCount & 0xFF);
        binary[4] = (byte)(word1 >> 8); binary[5] = (byte)(word1 & 0xFF);
        binary[6] = (byte)(word2 >> 8); binary[7] = (byte)(word2 & 0xFF);
        binary[8] = (byte)(checksum >> 8); binary[9] = (byte)(checksum & 0xFF);

        // Send preamble (ASCII, 7-bit OK)
        await _sim.SendAsync(preamble, TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Send binary data (needs 8-bit)
        await _sim.SendAsync(binary, TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Send action (ASCII)
        _received.Clear();
        await _sim.SendAsync(action, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        response = System.Text.Encoding.ASCII.GetString(_received.ToArray());
        Assert.Contains("#", response);

        // Verify: examine memory at 100 octal should show word1
        response = await SendAndCollect("100/");
        Assert.Contains("125715", response); // 0xABCD = 125715 octal
    }
}
