namespace IFTextCompactor;

public enum EntryKind
{
    Location,
    Object
}

// A single Location or Object, with its name and its descriptive text.
public sealed class Entry
{
    public required EntryKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
}

public enum Charset
{
    Ascii,
    Petscii
}

// One encoded, null-terminated output string, kept alongside the original
// text purely so the .asm emitter can attach a readable comment.
public sealed class CompressedString
{
    public required string Original { get; init; }
    public required byte[] Bytes { get; init; }
}

public sealed class CompressionResult
{
    public required List<CompressedString> Dictionary { get; init; }
    public required List<CompressedString> LocationNames { get; init; }
    public required List<CompressedString> LocationDescriptions { get; init; }
    public required List<CompressedString> ObjectNames { get; init; }
    public required List<CompressedString> ObjectDescriptions { get; init; }
}

// Uncompressed-vs-compressed byte accounting, shared by the console stats
// summary and the .asm header comment so the two never drift apart.
// Both totals include lo/hi index-table overhead, since that structure is
// needed either way - only dictionary substitution differs between them.
public sealed class CompressionStats
{
    public required int DictionaryCount { get; init; }
    public required long UncompressedContent { get; init; }
    public required long UncompressedIndex { get; init; }
    public required long CompressedContent { get; init; }
    public required long CompressedIndex { get; init; }

    public long UncompressedTotal => UncompressedContent + UncompressedIndex;
    public long CompressedTotal => CompressedContent + CompressedIndex;

    public static CompressionStats Compute(List<Entry> entries, CompressionResult result)
    {
        long uncompressedContent = 0;
        foreach (var e in entries)
        {
            uncompressedContent += e.Name.Length + 1;
            uncompressedContent += e.Description.Length + 1;
        }

        int locCount = entries.Count(e => e.Kind == EntryKind.Location);
        int objCount = entries.Count(e => e.Kind == EntryKind.Object);
        long uncompressedIndex = 4L * locCount + 4L * objCount;

        long compressedContent =
            result.Dictionary.Sum(x => (long)x.Bytes.Length) +
            result.LocationNames.Sum(x => (long)x.Bytes.Length) +
            result.LocationDescriptions.Sum(x => (long)x.Bytes.Length) +
            result.ObjectNames.Sum(x => (long)x.Bytes.Length) +
            result.ObjectDescriptions.Sum(x => (long)x.Bytes.Length);

        long compressedIndex =
            2L * result.Dictionary.Count +
            2L * result.LocationNames.Count +
            2L * result.LocationDescriptions.Count +
            2L * result.ObjectNames.Count +
            2L * result.ObjectDescriptions.Count;

        return new CompressionStats
        {
            DictionaryCount = result.Dictionary.Count,
            UncompressedContent = uncompressedContent,
            UncompressedIndex = uncompressedIndex,
            CompressedContent = compressedContent,
            CompressedIndex = compressedIndex
        };
    }
}
