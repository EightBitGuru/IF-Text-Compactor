using System.Text;

namespace IFTextCompactor;

public static class AsmEmitter
{
    private const int DictBytesPerRow = 12;
    private const int DescBytesPerRow = 32;
    private const int LabelsPerRow = 12;
    private const int CommentMaxColumn = 168;

    private enum BlockStyle
    {
        // Short strings (dictionary substitutions): one line for the byte data,
        // comment never needs wrapping.
        Standard,

        // Names: however long, all bytes stay on a single line.
        OneLine,

        // Descriptions: bytes wrap every DescBytesPerRow, comment wraps at
        // CommentMaxColumn.
        Wrapped
    }

    public static void Write(
        string path,
        ushort startAddress,
        string sourceFileName,
        CompressionResult result,
        CompressionStats stats)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// ============================================================");
        sb.AppendLine("// IF-Text-Compactor - generated string data. Do not hand-edit; regenerate");
        sb.AppendLine("// from source instead.");
        sb.AppendLine($"// Source file         : {sourceFileName}");
        sb.AppendLine($"// Generated           : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"// Dictionary entries  : {result.Dictionary.Count}");
        sb.AppendLine($"// Locations           : {result.LocationNames.Count}");
        sb.AppendLine($"// Objects             : {result.ObjectNames.Count}");
        sb.AppendLine($"// Uncompressed bytes  : {stats.UncompressedTotal:N0}");
        sb.AppendLine($"// Compressed bytes    : {stats.CompressedTotal:N0}");
        sb.AppendLine("// ============================================================");
        sb.AppendLine();
        sb.AppendLine($"* = ${startAddress:X4} \"IFTC data\"");
        sb.AppendLine();

        // All five string-data blocks first...
        var dictLabels = EmitStringBlock(sb, "Dictionary strings (selected via bit 7 of a text byte)", "Dict", result.Dictionary, BlockStyle.Standard);
        var locNameLabels = EmitStringBlock(sb, "Location names", "LocName", result.LocationNames, BlockStyle.OneLine);
        var locDescLabels = EmitStringBlock(sb, "Location descriptions", "LocDesc", result.LocationDescriptions, BlockStyle.Wrapped);
        var objNameLabels = EmitStringBlock(sb, "Object names", "ObjName", result.ObjectNames, BlockStyle.OneLine);
        var objDescLabels = EmitStringBlock(sb, "Object descriptions", "ObjDesc", result.ObjectDescriptions, BlockStyle.Wrapped);

        // ...then every address table together, built via .lohifill from a
        // label list rather than hand-written <label/>label byte rows.
        sb.AppendLine("// ------------------------------------------------------------");
        sb.AppendLine("// Address tables (lo/hi, generated with .lohifill).");
        sb.AppendLine("// Indexed access: <Prefix>.lo,x / <Prefix>.hi,x");
        sb.AppendLine("// ------------------------------------------------------------");
        sb.AppendLine();

        EmitAddressTable(sb, "Dict", dictLabels);
        EmitAddressTable(sb, "LocName", locNameLabels);
        EmitAddressTable(sb, "LocDesc", locDescLabels);
        EmitAddressTable(sb, "ObjName", objNameLabels);
        EmitAddressTable(sb, "ObjDesc", objDescLabels);

        ushort endAddress = (ushort)(startAddress + stats.CompressedTotal);
        sb.AppendLine("// ============================================================");
        sb.AppendLine($"// Data end address (next free byte): ${endAddress:X4}");
        sb.AppendLine("// ============================================================");

        File.WriteAllText(path, sb.ToString());
    }

    private static List<string> EmitStringBlock(
        StringBuilder sb,
        string sectionTitle,
        string labelPrefix,
        IReadOnlyList<CompressedString> items,
        BlockStyle style)
    {
        sb.AppendLine("// ------------------------------------------------------------");
        sb.AppendLine($"// {sectionTitle}");
        sb.AppendLine("// ------------------------------------------------------------");

        var labels = new List<string>();

        for (int i = 0; i < items.Count; i++)
        {
            string label = $"{labelPrefix}{i:D3}";
            labels.Add(label);

            string comment = EscapeComment(items[i].Original);

            switch (style)
            {
                case BlockStyle.OneLine:
                    sb.AppendLine($"{label}: // \"{comment}\"");
                    EmitAllBytesOneLine(sb, items[i].Bytes);
                    break;

                case BlockStyle.Wrapped:
                    EmitWrappedComment(sb, label, comment);
                    EmitByteRows(sb, items[i].Bytes, DescBytesPerRow);
                    break;

                case BlockStyle.Standard:
                default:
                    sb.AppendLine($"{label}: // \"{comment}\"");
                    EmitByteRows(sb, items[i].Bytes, DictBytesPerRow);
                    break;
            }
        }

        if (items.Count == 0)
            sb.AppendLine("    // (none)");

        sb.AppendLine();
        return labels;
    }

    private static void EmitWrappedComment(StringBuilder sb, string label, string comment)
    {
        string firstPrefix = $"{label}: // \"";
        // Right-align "//" with the first line's "//", then one extra space
        // makes up for the opening quote's column so the text itself also
        // lines up with the first line's text.
        string contPrefix = new string(' ', Math.Max(firstPrefix.Length - 4, 0)) + "//  ";
        const string suffix = "\"";

        int firstBudget = Math.Max(CommentMaxColumn - firstPrefix.Length - suffix.Length, 1);
        int contBudget = Math.Max(CommentMaxColumn - contPrefix.Length - suffix.Length, 1);

        var lines = WrapWords(comment, firstBudget, contBudget);

        for (int i = 0; i < lines.Count; i++)
        {
            string prefix = i == 0 ? firstPrefix : contPrefix;
            string closing = i == lines.Count - 1 ? suffix : "";
            sb.AppendLine(prefix + lines[i] + closing);
        }
    }

    private static List<string> WrapWords(string text, int firstBudget, int contBudget)
    {
        var lines = new List<string>();
        if (text.Length == 0)
        {
            lines.Add("");
            return lines;
        }

        var words = text.Split(' ');
        var current = new StringBuilder();
        int budget = firstBudget;

        foreach (var word in words)
        {
            int candidateLength = current.Length == 0 ? word.Length : current.Length + 1 + word.Length;
            if (current.Length > 0 && candidateLength > budget)
            {
                lines.Add(current.ToString());
                current.Clear();
                budget = contBudget;
            }

            if (current.Length > 0)
                current.Append(' ');
            current.Append(word);
        }

        if (current.Length > 0)
            lines.Add(current.ToString());

        return lines;
    }

    // Builds the address table for one block via a KickAssembler script-side
    // List() of the block's labels, fed into .lohifill - instead of manually
    // writing out <label/>label .byte rows for the lo and hi tables.
    private static void EmitAddressTable(StringBuilder sb, string labelPrefix, List<string> labels)
    {
        if (labels.Count == 0)
        {
            sb.AppendLine($"// {labelPrefix}: (none)");
            sb.AppendLine();
            return;
        }

        string listVar = $"{labelPrefix}Addrs";

        sb.AppendLine($"// {labelPrefix} address table (lo/hi, indexed 0..{labels.Count - 1})");
        sb.Append($".var {listVar} = List().add(");

        for (int i = 0; i < labels.Count; i += LabelsPerRow)
        {
            if (i > 0)
            {
                sb.AppendLine();
                sb.Append("    ");
            }

            var row = labels.Skip(i).Take(LabelsPerRow);
            sb.Append(string.Join(",", row));
            if (i + LabelsPerRow < labels.Count)
                sb.Append(',');
        }

        sb.AppendLine(")");
        sb.AppendLine($"{labelPrefix}: .lohifill {labels.Count}, {listVar}.get(i)");
        sb.AppendLine();
    }

    private static void EmitAllBytesOneLine(StringBuilder sb, byte[] bytes)
    {
        sb.AppendLine("    .byte " + string.Join(",", bytes.Select(b => $"${b:X2}")));
    }

    private static void EmitByteRows(StringBuilder sb, byte[] bytes, int rowWidth)
    {
        foreach (var row in ChunkBytes(bytes, rowWidth))
            sb.AppendLine("    .byte " + string.Join(",", row.Select(b => $"${b:X2}")));
    }

    // Chunks into rows of rowWidth bytes, except that a lone trailing null
    // terminator is folded into the previous row instead of sitting alone.
    private static List<byte[]> ChunkBytes(byte[] bytes, int rowWidth)
    {
        var rows = new List<byte[]>();
        for (int i = 0; i < bytes.Length; i += rowWidth)
        {
            int len = Math.Min(rowWidth, bytes.Length - i);
            rows.Add(bytes[i..(i + len)]);
        }

        if (rows.Count >= 2 && rows[^1].Length == 1)
        {
            rows[^2] = rows[^2].Concat(rows[^1]).ToArray();
            rows.RemoveAt(rows.Count - 1);
        }

        return rows;
    }

    private static string EscapeComment(string s) => s.Replace("\"", "'");
}
