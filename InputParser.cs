namespace IFTextCompactor;

// Input file format:
//
//   LOC:<name>
//   <description>
//
//   OBJ:<name>
//   <description>
//
// Entries are separated by one or more blank lines. Each tagged name line
// must be followed by exactly one description line (the next non-blank
// line). Tags are case-insensitive.
public static class InputParser
{
    public static List<Entry> Parse(string path)
    {
        string[] rawLines = File.ReadAllLines(path);
        var entries = new List<Entry>();

        int i = 0;
        while (i < rawLines.Length)
        {
            int lineNo = i + 1;
            string line = rawLines[i].TrimEnd();
            i++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            EntryKind kind;
            string rest;

            if (line.StartsWith("LOC:", StringComparison.OrdinalIgnoreCase))
            {
                kind = EntryKind.Location;
                rest = line[4..];
            }
            else if (line.StartsWith("OBJ:", StringComparison.OrdinalIgnoreCase))
            {
                kind = EntryKind.Object;
                rest = line[4..];
            }
            else
            {
                throw new FormatException(
                    $"Line {lineNo}: expected a name line starting with 'LOC:' or 'OBJ:', found: \"{line}\"");
            }

            string name = rest.Trim();
            if (name.Length == 0)
                throw new FormatException($"Line {lineNo}: tag present but name is empty.");

            while (i < rawLines.Length && string.IsNullOrWhiteSpace(rawLines[i]))
                i++;

            if (i >= rawLines.Length)
                throw new FormatException($"Line {lineNo}: entry \"{name}\" has no following description line.");

            int descLineNo = i + 1;
            string description = rawLines[i].TrimEnd();
            i++;

            if (description.StartsWith("LOC:", StringComparison.OrdinalIgnoreCase) ||
                description.StartsWith("OBJ:", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException(
                    $"Line {descLineNo}: expected a description for \"{name}\" but found another tagged entry instead.");
            }

            if (description.Length == 0)
                throw new FormatException($"Line {descLineNo}: description for \"{name}\" is empty.");

            entries.Add(new Entry { Kind = kind, Name = name, Description = description });
        }

        return entries;
    }
}
