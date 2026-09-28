using System;
using System.Collections.Generic;
using System.IO;

namespace RetroTerm.Core.Terminal.Printing;

/// <summary>
/// A print sink that keeps the pages in memory.
/// </summary>
/// <remarks>
/// <para><b>What it is for</b></para>
/// The printer that is always available: tests use it to read back exactly what was printed, and
/// a script or an MCP call can capture a print job without a file or a PDF writer anywhere in
/// sight. Core has no drawing library, so this is the only sink Core can offer.
///
/// <para><b>Pages</b></para>
/// A page ends on <see cref="FormFeed"/>. A page with nothing on it is not kept, because a
/// terminal with DECPFF set sends a form feed after every screen print and a run of them would
/// otherwise fill the job with blank sheets.
/// </remarks>
public sealed class MemoryPrintSink : IPrintSink
{
    private readonly List<byte[]> _pages = new List<byte[]>();
    private readonly MemoryStream _current = new MemoryStream();

    /// <summary>
    /// The completed pages, oldest first.
    /// </summary>
    /// <remarks>
    /// The page still being written is NOT here until a form feed or <see cref="EndJob"/> closes
    /// it, so a caller reading mid-job sees whole pages only.
    /// </remarks>
    public IReadOnlyList<byte[]> Pages => _pages;

    /// <summary>
    /// How many bytes are on the page being written.
    /// </summary>
    public int PendingLength => (int)_current.Length;

    /// <param name="data">
    /// The bytes, as received.
    /// </param>
    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;

#if NETSTANDARD2_1_OR_GREATER || NET
        _current.Write(data);
#else
        for (int i = 0; i < data.Length; i++) _current.WriteByte(data[i]);
#endif
    }

    /// <summary>
    /// Ends the page. An empty page is dropped rather than kept as a blank sheet.
    /// </summary>
    public void FormFeed()
    {
        if (_current.Length == 0) return;

        _pages.Add(_current.ToArray());
        _current.SetLength(0);
    }

    /// <summary>
    /// Closes whatever page is open. Harmless when there is nothing to close.
    /// </summary>
    public void EndJob()
    {
        FormFeed();
    }

    /// <summary>
    /// Everything printed so far, pages and all, as one run of bytes.
    /// </summary>
    /// <returns>
    /// The whole job.
    /// </returns>
    public byte[] ToArray()
    {
        int total = (int)_current.Length;
        for (int i = 0; i < _pages.Count; i++) total += _pages[i].Length;

        var all = new byte[total];
        int at = 0;

        for (int i = 0; i < _pages.Count; i++)
        {
            System.Buffer.BlockCopy(_pages[i], 0, all, at, _pages[i].Length);
            at += _pages[i].Length;
        }

        var pending = _current.GetBuffer();
        System.Buffer.BlockCopy(pending, 0, all, at, (int)_current.Length);

        return all;
    }

    /// <summary>
    /// Everything printed so far, read as text.
    /// </summary>
    /// <returns>
    /// The whole job decoded as UTF-8.
    /// </returns>
    public string ToText()
        => System.Text.Encoding.UTF8.GetString(ToArray());
}
