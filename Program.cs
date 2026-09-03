using IFTextCompactor;

string? inputPath = null;
ushort startAddress = 0x1000;
string? outputPath = null;
Charset charset = Charset.Ascii;

var argList = args.ToList();

try
{
    for (int i = 0; i < argList.Count; i++)
    {
        string a = argList[i];

        switch (a)
        {
            case "--start":
                i = RequireNext(argList, i, "--start");
                startAddress = ParseHexAddress(argList[i]);
                break;

            case "--out":
            case "--output":
                i = RequireNext(argList, i, "--out");
                outputPath = argList[i];
                break;

            case "--charset":
                i = RequireNext(argList, i, "--charset");
                charset = ParseCharset(argList[i]);
                break;

            case "-h":
            case "--help":
                PrintUsage();
                return 0;

            default:
                if (a.StartsWith("--", StringComparison.Ordinal))
                    throw new FormatException($"Unknown option: {a}");

                if (inputPath is not null)
                    throw new FormatException($"Unexpected extra argument: {a}");

                inputPath = a;
                break;
        }
    }
}
catch (FormatException ex)
{
    Console.Error.WriteLine($"Argument error: {ex.Message}");
    Console.Error.WriteLine();
    PrintUsage();
    return 1;
}

if (inputPath is null)
{
    PrintUsage();
    return 1;
}

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Input file not found: {inputPath}");
    return 1;
}

outputPath ??= inputPath + ".asm";

List<Entry> rawEntries;
try
{
    rawEntries = InputParser.Parse(inputPath);
}
catch (FormatException ex)
{
    Console.Error.WriteLine($"Parse error: {ex.Message}");
    return 1;
}

if (rawEntries.Count == 0)
{
    Console.Error.WriteLine("No entries found in input file.");
    return 1;
}

var entries = rawEntries
    .Select(e => new Entry
    {
        Kind = e.Kind,
        Name = TextNormalizer.Normalize(e.Name),
        Description = TextNormalizer.Normalize(e.Description)
    })
    .ToList();

try
{
    foreach (var e in entries)
    {
        TextNormalizer.ValidateAscii(e.Name, $"\"{e.Name}\" (name)");
        TextNormalizer.ValidateAscii(e.Description, $"\"{e.Name}\" (description)");
    }
}
catch (FormatException ex)
{
    Console.Error.WriteLine($"Character set error: {ex.Message}");
    return 1;
}

CompressionResult result;
CompressionStats stats;
try
{
    result = BpeCompressor.Compress(entries, charset);
    stats = CompressionStats.Compute(entries, result);
    AsmEmitter.Write(outputPath, startAddress, Path.GetFileName(inputPath), result, stats);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Output error: {ex.Message}");
    return 1;
}

PrintStats(inputPath, outputPath, startAddress, charset, entries, stats);

return 0;

// ------------------------------------------------------------

static int RequireNext(List<string> argList, int i, string optionName)
{
    if (i + 1 >= argList.Count)
        throw new FormatException($"Option {optionName} requires a value.");
    return i + 1;
}

static ushort ParseHexAddress(string s)
{
    string t = s.Trim();
    if (t.StartsWith("$", StringComparison.Ordinal))
        t = t[1..];
    else if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        t = t[2..];

    if (!ushort.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ushort value))
        throw new FormatException($"Invalid start address: \"{s}\" (expected hex, e.g. $1000).");

    return value;
}

static Charset ParseCharset(string s)
{
    return s.Trim().ToUpperInvariant() switch
    {
        "A" or "ASCII" => Charset.Ascii,
        "P" or "PETSCII" => Charset.Petscii,
        _ => throw new FormatException($"Invalid charset: \"{s}\" (expected A or P).")
    };
}

static void PrintUsage()
{
    Console.WriteLine("IF-Text-Compactor - text-adventure string compressor for 6502/VIC-20 targets");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  IFTC <inputfile> [--start $1000] [--out file.asm] [--charset A|P]");
    Console.WriteLine();
    Console.WriteLine("  <inputfile>     Text file with LOC:/OBJ: tagged name/description pairs,");
    Console.WriteLine("                  one tag+name line followed by one description line,");
    Console.WriteLine("                  entries separated by blank lines.");
    Console.WriteLine("  --start <hex>   16-bit start address, e.g. $1000 or 1000. Default $1000.");
    Console.WriteLine("  --out <path>    Output .asm path. Default: <inputfile>.asm");
    Console.WriteLine("  --charset A|P   A = plain ASCII bytes (default).");
    Console.WriteLine("                  P = VIC-20 PETSCII screen codes.");
}

static void PrintStats(
    string inputPath,
    string outputPath,
    ushort startAddress,
    Charset charset,
    List<Entry> entries,
    CompressionStats stats)
{
    long inputFileSize = new FileInfo(inputPath).Length;

    int locCount = entries.Count(e => e.Kind == EntryKind.Location);
    int objCount = entries.Count(e => e.Kind == EntryKind.Object);

    Console.WriteLine();
    Console.WriteLine("== IF-Text-Compactor ==");
    Console.WriteLine($"Input               : {inputPath}");
    Console.WriteLine($"Output              : {outputPath}");
    Console.WriteLine($"Start address       : ${startAddress:X4}");
    Console.WriteLine($"Charset             : {(charset == Charset.Petscii ? "PETSCII screen codes" : "ASCII")}");
    Console.WriteLine($"Entries             : {locCount} locations, {objCount} objects");
    Console.WriteLine($"Dictionary entries  : {stats.DictionaryCount} / 128");
    Console.WriteLine();
    Console.WriteLine($"Input file size     : {inputFileSize,8:N0} bytes");
    Console.WriteLine($"Uncompressed memory : {stats.UncompressedTotal,8:N0} bytes  (strings {stats.UncompressedContent:N0} + index tables {stats.UncompressedIndex:N0})");
    Console.WriteLine($"Compressed memory   : {stats.CompressedTotal,8:N0} bytes  (strings+dict {stats.CompressedContent:N0} + index tables {stats.CompressedIndex:N0})");

    if (stats.UncompressedTotal > 0)
    {
        double savedPct = 100.0 * (stats.UncompressedTotal - stats.CompressedTotal) / stats.UncompressedTotal;
        Console.WriteLine($"Saved               : {stats.UncompressedTotal - stats.CompressedTotal,8:N0} bytes  ({savedPct:0.0}%)");
    }
}
