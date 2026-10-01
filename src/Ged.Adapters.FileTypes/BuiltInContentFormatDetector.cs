using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Ged.Core.Ports.FileTypes;

namespace Ged.Adapters.FileTypes;

/// <summary>Identifies content from its bytes alone, with no third-party dependency.</summary>
/// <remarks>
/// <para>
/// Covers the formats this application accepts and nothing else, which is the whole point: a detector
/// written for a closed allowlist does not have the same job as one written to identify anything. The
/// first answers "is this what it claims to be?", the second "what is this?", and only the first is
/// needed to validate an upload.
/// </para>
/// <para>
/// Three techniques, because three families of format demand three:
/// </para>
/// <code>
/// leading bytes      pdf, png, jpeg, gif, tiff, bmp, rtf, exe, elf
/// ZIP container      docx, xlsx, pptx (and their macro-enabled twins), odt, ods, odp
/// OLE2 container     doc, xls, ppt — one signature, which .msi shares as well
/// </code>
/// <para>
/// Two of the leading signatures are only two bytes — <c>BM</c> and <c>MZ</c> — and are confirmed
/// against the structure behind them before being reported; otherwise ordinary text starting with
/// those letters would be refused as an image or as a program.
/// </para>
/// <para>
/// Executables are recognised deliberately, although no policy accepts one. Identifying a renamed
/// binary yields "this is an EXE" rather than "unrecognised", which is both a better message and a
/// stronger check for the text formats, whose condition for acceptance is precisely that nothing was
/// recognised.
/// </para>
/// <para>
/// Fixed-size reads go through <see cref="ReadInto"/>, which writes into a buffer the caller supplies
/// — usually on the stack. Only the variable-size reads, whose length comes from the file itself,
/// allocate.
/// </para>
/// </remarks>
public sealed class BuiltInContentFormatDetector : IContentFormatDetector
{
    private const int HeaderLength = 512;

    /// <summary>How far into a file the Windows header pointer may send the reader.</summary>
    /// <remarks>
    /// Linkers put it in the first kilobyte. The bound stops a forged pointer from turning a
    /// four-byte read into a seek across the whole uploaded file.
    /// </remarks>
    private const int MaxExecutableHeaderOffset = 64 * 1024;

    /// <summary>How many directory sectors of a compound file are followed.</summary>
    /// <remarks>
    /// Office documents hold their directory in one to a few sectors. Walking a chain through hostile
    /// input is bounded here, and a loop in that chain stops at the same bound.
    /// </remarks>
    private const int MaxCompoundDirectorySectors = 64;

    /// <summary>How many DIFAT sectors are walked to locate one allocation-table sector.</summary>
    private const int MaxCompoundDifatHops = 256;

    /// <summary>How many entries a ZIP may declare and still be a candidate Office document.</summary>
    /// <remarks>
    /// Real packages hold tens to a few thousand parts. The bound exists because reading a central
    /// directory costs memory per entry: without it, 16 MB of empty entries cost 112 MB to identify,
    /// and a 256 MB upload several gigabytes.
    /// </remarks>
    private const int MaxZipEntries = 10_000;

    /// <summary>The largest central directory read, in bytes.</summary>
    /// <remarks>
    /// Bounds what <see cref="MaxZipEntries"/> cannot: a single entry name may be 64 KB long.
    /// </remarks>
    private const int MaxCentralDirectoryLength = 2 * 1024 * 1024;

    /// <summary>The largest <c>[Content_Types].xml</c> read, compressed or not.</summary>
    /// <remarks>
    /// A few kilobytes in practice, a few tens for a presentation of several hundred slides. The bound
    /// applies to the inflated output, so a manifest whose data expands past it is abandoned at that
    /// length. Deflate tops out near 1030:1, so 256 KB of output bounds both the inflation of a
    /// compressed manifest and the memory an XML reader would spend loading it whole.
    /// </remarks>
    private const int MaxManifestLength = 256 * 1024;

    private const int EndOfCentralDirectoryLength = 22;
    private const int CentralHeaderLength = 46;
    private const int LocalHeaderLength = 30;
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint CentralHeaderSignature = 0x02014B50;
    private const uint LocalHeaderSignature = 0x04034B50;
    private const uint Zip64EndOfCentralDirectorySignature = 0x06064B50;
    private const uint Zip64LocatorSignature = 0x07064B50;
    private const int Zip64LocatorLength = 20;
    private const int Zip64EndOfCentralDirectoryLength = 56;
    private const ushort Zip64ExtraFieldId = 0x0001;
    private const ushort Sentinel16 = 0xFFFF;
    private const uint Sentinel32 = 0xFFFFFFFF;
    private const ushort Stored = 0;
    private const ushort Deflated = 8;
    private const ushort EncryptedFlag = 0x0001;

    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint MaxRegularSector = 0xFFFFFFFA;
    private const uint NoStream = 0xFFFFFFFF;
    private const int DirectoryEntryLength = 128;
    private const int HeaderDifatSlots = 109;
    private const int HeaderDifatOffset = 0x4C;

    private const string ManifestName = "[Content_Types].xml";
    private const string OpenDocumentMimeTypeName = "mimetype";

    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Ole2 = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static readonly DetectedFormat GenericZip = new("zip", "application/zip");
    private static readonly DetectedFormat GenericCompoundFile = new("ole2", "application/x-ole-storage");

    private static readonly DetectedFormat LegacyWord = new("doc", "application/msword");
    private static readonly DetectedFormat LegacyExcel = new("xls", "application/vnd.ms-excel");
    private static readonly DetectedFormat LegacyPowerPoint = new("ppt", "application/vnd.ms-powerpoint");

    /// <summary>The DIB header sizes a bitmap may carry, BITMAPCOREHEADER (12) to BITMAPV5HEADER (124).</summary>
    private static readonly HashSet<uint> BitmapInfoHeaderSizes = [12, 16, 40, 52, 56, 64, 108, 124];

    private static readonly (byte[] Signature, string Extension, string MediaType, SignatureConfirmation? Confirm)[] Leading =
    [
        ([0x25, 0x50, 0x44, 0x46], "pdf", "application/pdf", null),                       // %PDF
        ([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "png", "image/png", null),
        ([0xFF, 0xD8, 0xFF], "jpg", "image/jpeg", null),
        ([0x47, 0x49, 0x46, 0x38, 0x37, 0x61], "gif", "image/gif", null),                 // GIF87a
        ([0x47, 0x49, 0x46, 0x38, 0x39, 0x61], "gif", "image/gif", null),                 // GIF89a
        ([0x49, 0x49, 0x2A, 0x00], "tif", "image/tiff", null),                            // II*
        ([0x4D, 0x4D, 0x00, 0x2A], "tif", "image/tiff", null),                            // MM*
        ([0x42, 0x4D], "bmp", "image/bmp", IsBitmap),                                     // BM
        ([0x7B, 0x5C, 0x72, 0x74, 0x66], "rtf", "application/rtf", null),                 // {\rtf
        ([0x4D, 0x5A], "exe", "application/vnd.microsoft.portable-executable", IsWindowsExecutable), // MZ
        ([0x7F, 0x45, 0x4C, 0x46], "elf", "application/x-elf", null),                     // .ELF
    ];

    private static readonly Dictionary<string, DetectedFormat> OpenDocumentTypes = new(StringComparer.Ordinal)
    {
        ["application/vnd.oasis.opendocument.text"] =
            new("odt", "application/vnd.oasis.opendocument.text"),
        ["application/vnd.oasis.opendocument.spreadsheet"] =
            new("ods", "application/vnd.oasis.opendocument.spreadsheet"),
        ["application/vnd.oasis.opendocument.presentation"] =
            new("odp", "application/vnd.oasis.opendocument.presentation"),
    };

    /// <summary>Main-part content types, by the family each one identifies.</summary>
    /// <remarks>
    /// Templates, slideshows and add-ins are absent deliberately: they fall back to a plain ZIP and
    /// are refused as one, rather than being reported as the document type they resemble.
    /// </remarks>
    private static readonly Dictionary<string, OfficeFamily> MainPartTypes = new(StringComparer.Ordinal)
    {
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"] = OfficeFamily.Word,
        ["application/vnd.ms-word.document.macroEnabled.main+xml"] = OfficeFamily.Word,
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"] = OfficeFamily.Excel,
        ["application/vnd.ms-excel.sheet.macroEnabled.main+xml"] = OfficeFamily.Excel,
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"] = OfficeFamily.PowerPoint,
        ["application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml"] = OfficeFamily.PowerPoint,
    };

    /// <summary>Confirms a signature too short to mean anything on its own.</summary>
    /// <param name="header">The leading bytes already read.</param>
    /// <param name="content">The whole content, seekable, for a check that reaches past the header.</param>
    /// <returns>True when the structure expected behind the signature is really there.</returns>
    private delegate bool SignatureConfirmation(ReadOnlySpan<byte> header, Stream content);

    private enum OfficeFamily
    {
        Word,
        Excel,
        PowerPoint,
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">The stream cannot seek.</exception>
    /// <remarks>
    /// A stream that cannot seek is refused rather than read from wherever it stands: the port's
    /// contract requires one, and an answer computed from the middle of a file is worse than no
    /// answer at all.
    /// </remarks>
    public DetectedFormat? Detect(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!content.CanSeek)
        {
            throw new NotSupportedException("Content detection needs a seekable stream.");
        }

        content.Position = 0;

        Span<byte> buffer = stackalloc byte[HeaderLength];
        int read = content.ReadAtLeast(buffer, HeaderLength, throwOnEndOfStream: false);
        ReadOnlySpan<byte> header = buffer[..read];

        // Containers first: their signatures are shared, so a match here means "look inside", never
        // "done".
        if (header.StartsWith(Zip))
        {
            return DetectZipFlavour(content, header);
        }

        if (header.StartsWith(Ole2))
        {
            return DetectCompoundFileFlavour(content, header);
        }

        foreach ((byte[] signature, string extension, string mediaType, SignatureConfirmation? confirm) in Leading)
        {
            if (header.StartsWith(signature) && (confirm is null || confirm(header, content)))
            {
                return new DetectedFormat(extension, mediaType);
            }
        }

        // Plain text, CSV and JSON land here, and that is correct: they carry no signature at all,
        // and saying "nothing matched" is more honest than guessing.
        return null;
    }

    /// <summary>Confirms a bitmap behind the two-letter <c>BM</c> signature.</summary>
    /// <remarks>
    /// <c>BM</c> alone is two ASCII letters, and a CSV beginning <c>BMI,</c> used to be reported as an
    /// image — and refused as text. A real bitmap has its reserved bytes at zero, a known DIB header
    /// size, and pixel data starting after both headers and inside the file. Text satisfies none of
    /// the three. The declared file size is left to the catalogue, which already checks it.
    /// </remarks>
    private static bool IsBitmap(ReadOnlySpan<byte> header, Stream content)
    {
        if (header.Length < 18)
        {
            return false;
        }

        uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(header[6..]);
        uint pixelOffset = BinaryPrimitives.ReadUInt32LittleEndian(header[10..]);
        uint infoHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(header[14..]);

        return reserved == 0
            && BitmapInfoHeaderSizes.Contains(infoHeaderSize)
            && pixelOffset >= 14 + infoHeaderSize
            && pixelOffset <= content.Length;
    }

    /// <summary>Confirms a Windows executable behind the two-letter <c>MZ</c> signature.</summary>
    /// <remarks>
    /// <para>
    /// The DOS header points, at offset 0x3C, at the real header, whose signature is <c>PE\0\0</c>
    /// for every Win32 and .NET binary and <c>NE</c>, <c>LE</c> or <c>LX</c> for the older ones. Text
    /// beginning <c>MZ</c> has an ASCII pointer that lands on none of them.
    /// </para>
    /// <para>
    /// A bare DOS executable, with no extended header, is not identified. It stays refused as text —
    /// its DOS header is full of NUL bytes, which the text heuristic rejects — and refused as anything
    /// else, because it matches no accepted format.
    /// </para>
    /// </remarks>
    private static bool IsWindowsExecutable(ReadOnlySpan<byte> header, Stream content)
    {
        if (header.Length < 0x40)
        {
            return false;
        }

        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(header[0x3C..]);

        if (offset < 0x40 || offset > MaxExecutableHeaderOffset)
        {
            return false;
        }

        Span<byte> signature = stackalloc byte[4];

        if (offset + 4 <= header.Length)
        {
            header.Slice((int)offset, 4).CopyTo(signature);
        }
        else if (!ReadInto(content, offset, signature))
        {
            return false;
        }

        return signature.SequenceEqual("PE\0\0"u8)
            || signature[..2].SequenceEqual("NE"u8)
            || signature[..2].SequenceEqual("LE"u8)
            || signature[..2].SequenceEqual("LX"u8);
    }

    /// <summary>Tells apart the formats that share the ZIP signature.</summary>
    /// <remarks>
    /// <para>
    /// <c>PK\x03\x04</c> identifies a ZIP and nothing more. Every answer below is either a precise
    /// format proved by the archive's structure or <c>zip</c> — never an exception, never a guess.
    /// <c>zip</c> is a recognised format no policy accepts, so any doubt ends in a refusal.
    /// </para>
    /// <para>
    /// <see cref="ZipArchive"/> is deliberately not used. It materialises every central-directory entry
    /// before answering anything, and ignores the entry count the archive declares — measured: an
    /// archive declaring 10 entries while holding 200,000 is read in full, 112 MB, before it throws.
    /// Reading the directory here, under strict bounds, keeps identification at constant cost.
    /// </para>
    /// <para>
    /// The central directory is authoritative, because it is what Office and LibreOffice read. Local
    /// headers are consulted only to locate data.
    /// </para>
    /// </remarks>
    private static DetectedFormat DetectZipFlavour(Stream content, ReadOnlySpan<byte> header)
    {
        if (ReadCentralDirectory(content) is not { } entries)
        {
            return GenericZip;
        }

        if (entries.Count > 0 && entries[0].Name == OpenDocumentMimeTypeName)
        {
            return DetectOpenDocument(entries[0], header) ?? GenericZip;
        }

        return DetectOfficeOpenXml(content, entries) ?? GenericZip;
    }

    /// <summary>Identifies an OpenDocument package from its first entry.</summary>
    /// <remarks>
    /// The OpenDocument specification makes this test exact: the first entry is named <c>mimetype</c>,
    /// is stored uncompressed, and holds the media type verbatim. All three are required, so the answer
    /// comes from the header already read — no seek, no decompression. A package departing from any one
    /// of the three is not treated as OpenDocument, whatever it holds.
    /// </remarks>
    private static DetectedFormat? DetectOpenDocument(ZipEntryRecord mimetype, ReadOnlySpan<byte> header)
    {
        if (mimetype.LocalHeaderOffset != 0
            || mimetype.Method != Stored
            || mimetype.CompressedSize != mimetype.UncompressedSize
            || mimetype.UncompressedSize > 128
            || header.Length < LocalHeaderLength)
        {
            return null;
        }

        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[26..]);
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
        int dataStart = LocalHeaderLength + nameLength + extraLength;
        int dataEnd = dataStart + (int)mimetype.UncompressedSize;

        if (BinaryPrimitives.ReadUInt16LittleEndian(header[8..]) != Stored
            || dataEnd > header.Length
            || !header.Slice(LocalHeaderLength, nameLength).SequenceEqual("mimetype"u8))
        {
            return null;
        }

        string mediaType = Encoding.ASCII.GetString(header[dataStart..dataEnd]);

        return OpenDocumentTypes.GetValueOrDefault(mediaType);
    }

    /// <summary>Identifies an Office Open XML package from its content-types manifest.</summary>
    /// <remarks>
    /// <para>
    /// The manifest decides, not the part names. Office chooses whether to run macros from the main
    /// part's content type, so that is the fact a macro check has to read; and the main part may
    /// legitimately be called <c>document2.xml</c>, which defeats a check built on names alone.
    /// </para>
    /// <para>
    /// Any VBA signal — a macro-enabled content type, the VBA project's content type, or a part named
    /// <c>vbaProject.bin</c> — turns the answer into its macro-enabled twin. That is stricter than
    /// Office, which ignores a VBA part under a macro-free content type, and it is deliberate.
    /// </para>
    /// <para>
    /// Part names are indexed once before the loop. A manifest may declare as many overrides as the
    /// archive has entries, and confirming each one by scanning the list made identification quadratic
    /// on a large package.
    /// </para>
    /// </remarks>
    private static DetectedFormat? DetectOfficeOpenXml(Stream content, List<ZipEntryRecord> entries)
    {
        ZipEntryRecord? manifest = entries.Find(e => e.Name == ManifestName);

        if (manifest is null || ReadManifest(content, manifest) is not { } contentTypes)
        {
            return null;
        }

        OfficeFamily? family = ResolveOfficeFamily(entries, contentTypes);

        if (family is not { } resolved)
        {
            return null;
        }

        bool hasMacros = contentTypes.Exists(
                t => t.ContentType.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase)
                     || t.ContentType.Equals("application/vnd.ms-office.vbaProject", StringComparison.OrdinalIgnoreCase))
            || entries.Exists(e => e.Name.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase));

        return (resolved, hasMacros) switch
        {
            (OfficeFamily.Word, false) => new(
                "docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            (OfficeFamily.Word, true) => new(
                "docm", "application/vnd.ms-word.document.macroEnabled.12"),
            (OfficeFamily.Excel, false) => new(
                "xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            (OfficeFamily.Excel, true) => new(
                "xlsm", "application/vnd.ms-excel.sheet.macroEnabled.12"),
            (OfficeFamily.PowerPoint, false) => new(
                "pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
            (OfficeFamily.PowerPoint, true) => new(
                "pptm", "application/vnd.ms-powerpoint.presentation.macroEnabled.12"),
            _ => null,
        };
    }

    /// <summary>Resolves the Office family the manifest declares, or null when none is.</summary>
    /// <remarks>
    /// A main part the manifest names but the archive does not hold proves nothing, and two main parts
    /// from different families do not make a document anyone can name.
    /// </remarks>
    private static OfficeFamily? ResolveOfficeFamily(
        List<ZipEntryRecord> entries,
        List<(string? PartName, string ContentType)> contentTypes)
    {
        HashSet<string>? partNames = null;
        OfficeFamily? family = null;

        foreach ((string? partName, string contentType) in contentTypes)
        {
            if (partName is null || !MainPartTypes.TryGetValue(contentType, out OfficeFamily found))
            {
                continue;
            }

            partNames ??= BuildPartNames(entries);

            if (!partNames.Contains(partName))
            {
                continue;
            }

            if (family is not null && family != found)
            {
                return null;
            }

            family = found;
        }

        return family;

        static HashSet<string> BuildPartNames(List<ZipEntryRecord> entries)
        {
            var names = new HashSet<string>(entries.Count, StringComparer.OrdinalIgnoreCase);

            foreach (ZipEntryRecord entry in entries)
            {
                names.Add("/" + entry.Name);
            }

            return names;
        }
    }

    /// <summary>Reads the central directory, or returns null when it cannot be trusted.</summary>
    /// <remarks>
    /// <para>
    /// ZIP64 is read, not refused. The specification allows it even where no limit is exceeded, and
    /// some tools use it anyway — Info-ZIP as soon as its input is streamed, DotNetZip on every entry.
    /// A document repackaged by either is legitimate, so the 64-bit values are resolved and then held
    /// to the same bounds as everything else.
    /// </para>
    /// <para>
    /// Every other structural doubt returns null rather than being repaired: a multi-volume archive,
    /// data before the first entry, bytes after the end record, 32-bit and 64-bit records that
    /// disagree, an entry count or a directory size past the bounds, a header running off the end of
    /// the directory. Office documents are single files written in one pass; archives that are not are
    /// not what an Office document claims to be.
    /// </para>
    /// <para>
    /// Rejecting data before the first entry also closes the self-extracting shape, where a ZIP hides
    /// behind a prefix another reader would take for the real file.
    /// </para>
    /// </remarks>
    private static List<ZipEntryRecord>? ReadCentralDirectory(Stream content)
    {
        long length = content.Length;
        int tailLength = (int)Math.Min(length, EndOfCentralDirectoryLength + ushort.MaxValue);

        if (ReadAt(content, length - tailLength, tailLength) is not { } tail)
        {
            return null;
        }

        int eocd = FindEndRecord(tail);

        if (eocd < 0)
        {
            return null;
        }

        long eocdPosition = length - tailLength + eocd;
        EndRecord classic = ReadClassicEndRecord(tail.AsSpan(eocd), eocdPosition);
        EndRecord? end = ResolveEndRecord(content, eocdPosition, classic);

        if (end is null || !IsUsable(end))
        {
            return null;
        }

        long directoryOffset = (long)end.DirectoryOffset;

        if (ReadAt(content, directoryOffset, (int)end.DirectorySize) is not { } directory)
        {
            return null;
        }

        return ParseEntries(directory, (int)end.TotalEntries, directoryOffset);
    }

    /// <summary>Locates the end record, or -1 when there is no usable one.</summary>
    /// <remarks>
    /// The record is searched for from the end, and its comment must reach exactly the end of the file:
    /// a signature planted inside the comment cannot shadow it, and nothing can follow it.
    /// </remarks>
    private static int FindEndRecord(ReadOnlySpan<byte> tail)
    {
        for (int i = tail.Length - EndOfCentralDirectoryLength; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail[i..]) == EndOfCentralDirectorySignature
                && i + EndOfCentralDirectoryLength + BinaryPrimitives.ReadUInt16LittleEndian(tail[(i + 20)..]) == tail.Length)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Reads the fields of the 32-bit end record.</summary>
    private static EndRecord ReadClassicEndRecord(ReadOnlySpan<byte> record, long eocdPosition) =>
        new(
            Disk: BinaryPrimitives.ReadUInt16LittleEndian(record[4..]),
            DirectoryDisk: BinaryPrimitives.ReadUInt16LittleEndian(record[6..]),
            EntriesOnDisk: BinaryPrimitives.ReadUInt16LittleEndian(record[8..]),
            TotalEntries: BinaryPrimitives.ReadUInt16LittleEndian(record[10..]),
            DirectorySize: BinaryPrimitives.ReadUInt32LittleEndian(record[12..]),
            DirectoryOffset: BinaryPrimitives.ReadUInt32LittleEndian(record[16..]),
            End: (ulong)eocdPosition);

    /// <summary>Chooses between the 32-bit record and its ZIP64 counterpart.</summary>
    /// <remarks>
    /// A locator that is present but refused does not allow a fallback to the narrow fields: the
    /// archive declared 64-bit values, and reading the others would describe a different archive.
    /// </remarks>
    private static EndRecord? ResolveEndRecord(Stream content, long eocdPosition, EndRecord classic) =>
        ReadZip64EndRecord(content, eocdPosition, classic) is { } zip64
            ? zip64
            : HasZip64Locator(content, eocdPosition) ? null : classic;

    /// <summary>Checks that the end record describes an archive this detector can read.</summary>
    /// <remarks>
    /// The order of the conditions matters: the size is bounded before being added to the offset, so
    /// the last comparison cannot overflow.
    /// </remarks>
    private static bool IsUsable(EndRecord end) =>
        end.Disk == 0
        && end.DirectoryDisk == 0
        && end.EntriesOnDisk == end.TotalEntries
        && end.TotalEntries <= MaxZipEntries
        && end.DirectorySize <= MaxCentralDirectoryLength
        && end.DirectoryOffset <= end.End
        && end.DirectoryOffset + end.DirectorySize == end.End;

    /// <summary>Splits the central directory into entries, or returns null at the first disagreement.</summary>
    /// <remarks>
    /// Bytes left over mean the declared count and the directory disagree — the inconsistency
    /// <see cref="ZipArchive"/> reports only after reading everything.
    /// </remarks>
    private static List<ZipEntryRecord>? ParseEntries(byte[] directory, int totalEntries, long directoryOffset)
    {
        var entries = new List<ZipEntryRecord>(totalEntries);
        int position = 0;

        for (int i = 0; i < totalEntries; i++)
        {
            if (ParseEntry(directory, position, directoryOffset) is not var (entry, recordLength))
            {
                return null;
            }

            entries.Add(entry);
            position += recordLength;
        }

        return position == directory.Length && (entries.Count == 0 || entries[0].LocalHeaderOffset == 0)
            ? entries
            : null;
    }

    /// <summary>Reads one central-directory entry at a given position.</summary>
    /// <returns>The entry and the length of its record, or null when it cannot be read.</returns>
    private static (ZipEntryRecord Entry, int RecordLength)? ParseEntry(
        ReadOnlySpan<byte> directory, int position, long directoryOffset)
    {
        if (position + CentralHeaderLength > directory.Length)
        {
            return null;
        }

        ReadOnlySpan<byte> entry = directory[position..];

        if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != CentralHeaderSignature)
        {
            return null;
        }

        ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[28..]);
        ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[30..]);
        ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[32..]);
        int recordLength = CentralHeaderLength + nameLength + extraLength + commentLength;

        if (position + recordLength > directory.Length)
        {
            return null;
        }

        (long Uncompressed, long Compressed, long LocalHeaderOffset)? resolved = ResolveZip64Sizes(
            BinaryPrimitives.ReadUInt32LittleEndian(entry[24..]),
            BinaryPrimitives.ReadUInt32LittleEndian(entry[20..]),
            BinaryPrimitives.ReadUInt32LittleEndian(entry[42..]),
            BinaryPrimitives.ReadUInt16LittleEndian(entry[34..]),
            entry.Slice(CentralHeaderLength + nameLength, extraLength));

        if (resolved is not { } sizes || sizes.LocalHeaderOffset >= directoryOffset)
        {
            return null;
        }

        var record = new ZipEntryRecord(
            Encoding.UTF8.GetString(entry.Slice(CentralHeaderLength, nameLength)),
            BinaryPrimitives.ReadUInt16LittleEndian(entry[8..]),
            BinaryPrimitives.ReadUInt16LittleEndian(entry[10..]),
            sizes.Compressed,
            sizes.Uncompressed,
            sizes.LocalHeaderOffset);

        return (record, recordLength);
    }

    /// <summary>Reads the ZIP64 end record, or null when the archive has none or it cannot be trusted.</summary>
    /// <remarks>
    /// Every 32-bit field must either hold the sentinel or agree with its 64-bit counterpart — Info-ZIP
    /// writes both. Two records that disagree describe two archives, and a reader that picks one of
    /// them is exactly the reader an attacker is aiming at.
    /// </remarks>
    private static EndRecord? ReadZip64EndRecord(Stream content, long eocdPosition, EndRecord classic)
    {
        long locatorPosition = eocdPosition - Zip64LocatorLength;
        Span<byte> locator = stackalloc byte[Zip64LocatorLength];

        if (locatorPosition < 0
            || !ReadInto(content, locatorPosition, locator)
            || BinaryPrimitives.ReadUInt32LittleEndian(locator) != Zip64LocatorSignature)
        {
            return null;
        }

        uint recordDisk = BinaryPrimitives.ReadUInt32LittleEndian(locator[4..]);
        ulong recordOffset = BinaryPrimitives.ReadUInt64LittleEndian(locator[8..]);
        uint totalDisks = BinaryPrimitives.ReadUInt32LittleEndian(locator[16..]);
        Span<byte> record = stackalloc byte[Zip64EndOfCentralDirectoryLength];

        if (recordDisk != 0
            || totalDisks > 1
            || recordOffset + 12 > (ulong)locatorPosition
            || !ReadInto(content, (long)recordOffset, record)
            || BinaryPrimitives.ReadUInt32LittleEndian(record) != Zip64EndOfCentralDirectorySignature)
        {
            return null;
        }

        // The size excludes the first 12 bytes. It has to close exactly on the locator, checked
        // without an addition that a forged size could overflow.
        ulong recordSize = BinaryPrimitives.ReadUInt64LittleEndian(record[4..]);

        if (recordSize < Zip64EndOfCentralDirectoryLength - 12
            || recordSize != (ulong)locatorPosition - recordOffset - 12)
        {
            return null;
        }

        var zip64 = new EndRecord(
            Disk: BinaryPrimitives.ReadUInt32LittleEndian(record[16..]),
            DirectoryDisk: BinaryPrimitives.ReadUInt32LittleEndian(record[20..]),
            EntriesOnDisk: BinaryPrimitives.ReadUInt64LittleEndian(record[24..]),
            TotalEntries: BinaryPrimitives.ReadUInt64LittleEndian(record[32..]),
            DirectorySize: BinaryPrimitives.ReadUInt64LittleEndian(record[40..]),
            DirectoryOffset: BinaryPrimitives.ReadUInt64LittleEndian(record[48..]),
            End: recordOffset);

        bool agrees = Agrees(classic.Disk, Sentinel16, zip64.Disk)
            && Agrees(classic.DirectoryDisk, Sentinel16, zip64.DirectoryDisk)
            && Agrees(classic.EntriesOnDisk, Sentinel16, zip64.EntriesOnDisk)
            && Agrees(classic.TotalEntries, Sentinel16, zip64.TotalEntries)
            && Agrees(classic.DirectorySize, Sentinel32, zip64.DirectorySize)
            && Agrees(classic.DirectoryOffset, Sentinel32, zip64.DirectoryOffset);

        return agrees ? zip64 : null;

        static bool Agrees(ulong narrow, ulong sentinel, ulong wide) => narrow == sentinel || narrow == wide;
    }

    /// <summary>Whether a ZIP64 locator precedes the end record, valid or not.</summary>
    /// <remarks>
    /// A locator <see cref="ReadZip64EndRecord"/> refused must not lead to a fallback on the 32-bit
    /// record: the archive declared 64-bit values, and reading the narrow ones instead would describe
    /// a different archive.
    /// </remarks>
    private static bool HasZip64Locator(Stream content, long eocdPosition)
    {
        Span<byte> signature = stackalloc byte[4];

        return eocdPosition >= Zip64LocatorLength
            && ReadInto(content, eocdPosition - Zip64LocatorLength, signature)
            && BinaryPrimitives.ReadUInt32LittleEndian(signature) == Zip64LocatorSignature;
    }

    /// <summary>Replaces sentinel sizes and offset with their ZIP64 values, or null when those are absent.</summary>
    /// <remarks>
    /// The extra field lists only the values whose 32-bit slot holds the sentinel, always in the same
    /// order: uncompressed size, compressed size, local header offset, start disk. A sentinel without
    /// its value, or a start disk other than zero, is not an entry this detector can locate.
    /// </remarks>
    private static (long Uncompressed, long Compressed, long LocalHeaderOffset)? ResolveZip64Sizes(
        uint uncompressed, uint compressed, uint localHeaderOffset, ushort startDisk, ReadOnlySpan<byte> extra)
    {
        int needed = (uncompressed == Sentinel32 ? 8 : 0)
            + (compressed == Sentinel32 ? 8 : 0)
            + (localHeaderOffset == Sentinel32 ? 8 : 0)
            + (startDisk == Sentinel16 ? 4 : 0);

        if (needed == 0)
        {
            return startDisk == 0 ? (uncompressed, compressed, localHeaderOffset) : null;
        }

        ReadOnlySpan<byte> field = FindExtraField(extra, Zip64ExtraFieldId);

        if (field.Length < needed)
        {
            return null;
        }

        long resolvedUncompressed = uncompressed == Sentinel32 ? Next(ref field) : uncompressed;
        long resolvedCompressed = compressed == Sentinel32 ? Next(ref field) : compressed;
        long resolvedOffset = localHeaderOffset == Sentinel32 ? Next(ref field) : localHeaderOffset;
        uint resolvedDisk = startDisk == Sentinel16 ? BinaryPrimitives.ReadUInt32LittleEndian(field) : startDisk;

        return resolvedUncompressed < 0 || resolvedCompressed < 0 || resolvedOffset < 0 || resolvedDisk != 0
            ? null
            : (resolvedUncompressed, resolvedCompressed, resolvedOffset);

        static long Next(ref ReadOnlySpan<byte> values)
        {
            long value = BinaryPrimitives.ReadInt64LittleEndian(values);
            values = values[8..];
            return value;
        }
    }

    /// <summary>Locates an extra field by its identifier, or yields an empty span.</summary>
    /// <remarks>
    /// An empty span means "absent": the caller compares its length against what it needs, and a zero
    /// length fails that comparison exactly as a truncated field would.
    /// </remarks>
    private static ReadOnlySpan<byte> FindExtraField(ReadOnlySpan<byte> extra, ushort wanted)
    {
        while (extra.Length >= 4)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra);
            ushort size = BinaryPrimitives.ReadUInt16LittleEndian(extra[2..]);

            if (4 + size > extra.Length)
            {
                return default;
            }

            if (id == wanted)
            {
                return extra.Slice(4, size);
            }

            extra = extra[(4 + size)..];
        }

        return default;
    }

    /// <summary>Reads the manifest's declarations — part name for overrides, content type for all.</summary>
    /// <remarks>
    /// Decompression is bounded on the output, not on the declared size: a declared size is written by
    /// whoever built the file. The XML reader prohibits DTDs and resolves nothing, so no entity can
    /// expand and no external resource can be fetched.
    /// </remarks>
    private static List<(string? PartName, string ContentType)>? ReadManifest(Stream content, ZipEntryRecord manifest)
    {
        if ((manifest.Flags & EncryptedFlag) != 0
            || manifest.Method is not (Stored or Deflated)
            || manifest.CompressedSize > MaxManifestLength
            || manifest.UncompressedSize > MaxManifestLength)
        {
            return null;
        }

        Span<byte> local = stackalloc byte[LocalHeaderLength];

        if (!ReadInto(content, manifest.LocalHeaderOffset, local)
            || BinaryPrimitives.ReadUInt32LittleEndian(local) != LocalHeaderSignature)
        {
            return null;
        }

        long dataOffset = manifest.LocalHeaderOffset + (long)LocalHeaderLength
            + BinaryPrimitives.ReadUInt16LittleEndian(local[26..])
            + BinaryPrimitives.ReadUInt16LittleEndian(local[28..]);

        if (ReadAt(content, dataOffset, (int)manifest.CompressedSize) is not { } compressed)
        {
            return null;
        }

        byte[] xml;
        try
        {
            xml = manifest.Method == Stored ? compressed : Inflate(compressed, MaxManifestLength);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        return xml.Length == manifest.UncompressedSize ? ReadContentTypes(xml) : null;
    }

    /// <summary>Extracts the (part, content type) pairs from the already inflated manifest.</summary>
    private static List<(string? PartName, string ContentType)>? ReadContentTypes(byte[] xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxManifestLength,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        var contentTypes = new List<(string? PartName, string ContentType)>();

        try
        {
            using var reader = XmlReader.Create(new MemoryStream(xml, writable: false), settings);

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element
                    && reader.LocalName is "Default" or "Override"
                    && reader.GetAttribute("ContentType") is { } contentType)
                {
                    contentTypes.Add((reader.GetAttribute("PartName"), contentType));
                }
            }
        }
        catch (XmlException)
        {
            return null;
        }

        return contentTypes;
    }

    /// <summary>Inflates at most <paramref name="limit"/> bytes, plus one to detect an overrun.</summary>
    /// <remarks>
    /// The working buffer comes from the pool: it is always the size of the bound, while a real
    /// manifest fills a few per cent of it. Only the result, at its exact size, is allocated.
    /// </remarks>
    private static byte[] Inflate(byte[] compressed, int limit)
    {
        using var inflater = new DeflateStream(new MemoryStream(compressed, writable: false), CompressionMode.Decompress);
        byte[] output = ArrayPool<byte>.Shared.Rent(limit + 1);

        try
        {
            // One byte past the limit means the entry inflates past it; the caller's size check then
            // fails, because no accepted manifest is that large.
            int read = inflater.ReadAtLeast(output.AsSpan(0, limit + 1), limit + 1, throwOnEndOfStream: false);

            return output.AsSpan(0, read).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(output);
        }
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes at an offset, or null when they are not there.</summary>
    /// <remarks>
    /// For reads whose length comes from the file. Where the length is known at compile time,
    /// <see cref="ReadInto"/> avoids the allocation.
    /// </remarks>
    private static byte[]? ReadAt(Stream content, long offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > content.Length)
        {
            return null;
        }

        byte[] buffer = new byte[count];

        return ReadInto(content, offset, buffer) ? buffer : null;
    }

    /// <summary>Reads enough to fill <paramref name="buffer"/> at a given offset.</summary>
    /// <returns>True when the buffer was filled completely.</returns>
    private static bool ReadInto(Stream content, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > content.Length)
        {
            return false;
        }

        content.Position = offset;

        return content.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;
    }

    /// <summary>Tells apart the legacy Office formats that share the OLE2 signature.</summary>
    /// <remarks>
    /// <para>
    /// <c>D0 CF 11 E0</c> identifies the container: .doc, .xls, .ppt and .msi are one format at that
    /// level. The flavour comes from the name of a stream sitting directly under the root storage.
    /// </para>
    /// <para>
    /// Only the root's direct children count. An .xls embedding a Word document also holds a
    /// <c>WordDocument</c> stream, one storage further down; a byte scan cannot tell the two apart, and
    /// used to find the name in the document's text as readily as in the directory.
    /// </para>
    /// <para>
    /// The directory chain is followed through the allocation table, under strict bounds on sectors,
    /// DIFAT hops and tree size. Any inconsistency yields <c>ole2</c> — a recognised container no
    /// policy accepts — rather than a guess or an exception.
    /// </para>
    /// </remarks>
    private static DetectedFormat DetectCompoundFileFlavour(Stream content, ReadOnlySpan<byte> header)
    {
        if (ReadCompoundRootStreams(content, header) is not { } streams)
        {
            return GenericCompoundFile;
        }

        DetectedFormat? match = null;
        int matches = 0;

        if (streams.Contains("WordDocument"))
        {
            match = LegacyWord;
            matches++;
        }

        if (streams.Contains("Workbook"))
        {
            match = LegacyExcel;
            matches++;
        }

        if (streams.Contains("PowerPoint Document"))
        {
            match = LegacyPowerPoint;
            matches++;
        }

        // None: a .msi or another container. Several: a file claiming two formats at once.
        return matches == 1 ? match! : GenericCompoundFile;
    }

    /// <summary>Reads the names of the streams directly under the root storage, or null when they cannot be trusted.</summary>
    /// <remarks>
    /// Header layout per [MS-CFB] 2.2: version 3 uses 512-byte sectors, version 4 uses 4096-byte
    /// sectors, and the two fields must agree. Sector <c>n</c> starts at <c>(n + 1) × sector size</c>.
    /// </remarks>
    private static HashSet<string>? ReadCompoundRootStreams(Stream content, ReadOnlySpan<byte> header)
    {
        if (header.Length < 512)
        {
            return null;
        }

        ushort majorVersion = BinaryPrimitives.ReadUInt16LittleEndian(header[0x1A..]);
        ushort byteOrder = BinaryPrimitives.ReadUInt16LittleEndian(header[0x1C..]);
        ushort sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(header[0x1E..]);

        if (byteOrder != 0xFFFE || !((majorVersion == 3 && sectorShift == 9) || (majorVersion == 4 && sectorShift == 12)))
        {
            return null;
        }

        if (ReadDirectory(content, header, sectorShift) is not { } entries)
        {
            return null;
        }

        // Entry 0 is the root storage (type 5); its child is the top of a tree of the root's own
        // children, linked by left and right siblings.
        return entries.Length >= DirectoryEntryLength && entries[0x42] == 5
            ? CollectRootStreams(entries)
            : null;
    }

    /// <summary>Reads the directory sectors, in chain order, or null at the first doubt.</summary>
    /// <remarks>
    /// The chain is walked before it is read: the sector count is then known, and the directory fits in
    /// a single allocation of its exact size rather than a list that doubles and copies itself. A
    /// sector out of bounds fails one pass or the other, and both answer null.
    /// </remarks>
    private static byte[]? ReadDirectory(Stream content, ReadOnlySpan<byte> header, int sectorShift)
    {
        Span<uint> chain = stackalloc uint[MaxCompoundDirectorySectors];
        int count = 0;
        uint sector = BinaryPrimitives.ReadUInt32LittleEndian(header[0x30..]);

        while (sector != EndOfChain)
        {
            if (sector > MaxRegularSector || count == MaxCompoundDirectorySectors || Contains(chain[..count], sector))
            {
                return null;
            }

            chain[count++] = sector;

            if (NextSector(content, header, sectorShift, sector) is not { } next)
            {
                return null;
            }

            sector = next;
        }

        int sectorSize = 1 << sectorShift;
        byte[] directory = new byte[count * sectorSize];

        for (int i = 0; i < count; i++)
        {
            if (!ReadInto(content, ((long)chain[i] + 1) << sectorShift, directory.AsSpan(i * sectorSize, sectorSize)))
            {
                return null;
            }
        }

        return directory;

        static bool Contains(ReadOnlySpan<uint> seen, uint sector) => seen.IndexOf(sector) >= 0;
    }

    /// <summary>Walks the tree of the root's children and yields the names of those that are streams.</summary>
    /// <remarks>
    /// Only left and right siblings are followed: descending into an entry's own child would reach the
    /// streams of a nested storage, which the root does not hold directly.
    /// </remarks>
    private static HashSet<string>? CollectRootStreams(byte[] entries)
    {
        int entryCount = entries.Length / DirectoryEntryLength;
        var streams = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<uint>();
        var pending = new Stack<uint>();
        pending.Push(BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan(0x4C)));

        while (pending.Count > 0)
        {
            uint id = pending.Pop();

            if (id == NoStream)
            {
                continue;
            }

            if (id == 0 || id >= entryCount || !seen.Add(id))
            {
                return null;
            }

            ReadOnlySpan<byte> entry = entries.AsSpan((int)id * DirectoryEntryLength, DirectoryEntryLength);
            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[0x40..]);

            if (nameLength is < 2 or > 64 || nameLength % 2 != 0)
            {
                return null;
            }

            if (entry[0x42] == 2)
            {
                streams.Add(Encoding.Unicode.GetString(entry[..(nameLength - 2)]));
            }

            pending.Push(BinaryPrimitives.ReadUInt32LittleEndian(entry[0x44..]));
            pending.Push(BinaryPrimitives.ReadUInt32LittleEndian(entry[0x48..]));
        }

        return streams;
    }

    /// <summary>The allocation-table entry for a sector, or null when it falls out of bounds.</summary>
    private static uint? NextSector(Stream content, ReadOnlySpan<byte> header, int sectorShift, uint current)
    {
        uint entriesPerSector = (uint)(1 << sectorShift) / 4;

        if (FatSectorFor(content, header, sectorShift, current / entriesPerSector) is not { } fatSector
            || fatSector > MaxRegularSector)
        {
            return null;
        }

        Span<byte> value = stackalloc byte[4];
        long offset = (((long)fatSector + 1) << sectorShift) + (4 * (current % entriesPerSector));

        return ReadInto(content, offset, value) ? BinaryPrimitives.ReadUInt32LittleEndian(value) : null;
    }

    /// <summary>Locates an index's allocation-table sector, through the header DIFAT and then its chain.</summary>
    /// <remarks>
    /// The first 109 indexes are in the header already read. Past those, each DIFAT sector holds
    /// <c>entriesPerSector - 1</c> indexes, its last slot pointing at the next one.
    /// </remarks>
    private static uint? FatSectorFor(Stream content, ReadOnlySpan<byte> header, int sectorShift, uint fatIndex)
    {
        if (fatIndex < HeaderDifatSlots)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(header[(HeaderDifatOffset + (4 * (int)fatIndex))..]);
        }

        uint entriesPerSector = (uint)(1 << sectorShift) / 4;
        uint remaining = fatIndex - HeaderDifatSlots;
        uint difatSector = BinaryPrimitives.ReadUInt32LittleEndian(header[0x44..]);
        Span<byte> slot = stackalloc byte[4];
        int hops = 0;

        while (remaining >= entriesPerSector - 1)
        {
            long link = (((long)difatSector + 1) << sectorShift) + (1 << sectorShift) - 4;

            if (++hops > MaxCompoundDifatHops || difatSector > MaxRegularSector || !ReadInto(content, link, slot))
            {
                return null;
            }

            difatSector = BinaryPrimitives.ReadUInt32LittleEndian(slot);
            remaining -= entriesPerSector - 1;
        }

        long offset = (((long)difatSector + 1) << sectorShift) + (4 * remaining);

        return difatSector <= MaxRegularSector && ReadInto(content, offset, slot)
            ? BinaryPrimitives.ReadUInt32LittleEndian(slot)
            : null;
    }

    /// <summary>What the central directory states about an entry — the only facts identification needs.</summary>
    private sealed record ZipEntryRecord(
        string Name,
        ushort Flags,
        ushort Method,
        long CompressedSize,
        long UncompressedSize,
        long LocalHeaderOffset);

    /// <summary>The end record's facts, widened so the 32-bit and ZIP64 shapes share one check.</summary>
    /// <param name="Disk">The number of this disk.</param>
    /// <param name="DirectoryDisk">The disk the central directory starts on.</param>
    /// <param name="EntriesOnDisk">The entries present on this disk.</param>
    /// <param name="TotalEntries">The entries of the whole archive.</param>
    /// <param name="DirectorySize">The length of the central directory, in bytes.</param>
    /// <param name="DirectoryOffset">Where the central directory starts.</param>
    /// <param name="End">Where the record following the central directory starts.</param>
    private sealed record EndRecord(
        ulong Disk,
        ulong DirectoryDisk,
        ulong EntriesOnDisk,
        ulong TotalEntries,
        ulong DirectorySize,
        ulong DirectoryOffset,
        ulong End);
}
