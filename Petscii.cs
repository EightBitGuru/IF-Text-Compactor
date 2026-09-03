namespace IFTextCompactor;

// Best-effort ASCII -> VIC-20/C64 "upper/lowercase" character-set SCREEN CODE
// mapping, for use when the game selects the upper/lowercase chargen bank at
// runtime (needed to display mixed-case room and object text).
//
// IMPORTANT: screen-code layouts differ between the two built-in character
// sets (uppercase/graphics vs upper/lowercase), and this table has not been
// checked against real hardware or a VICE screen-code reference - verify it
// against your own print routine and chargen bank selection before trusting
// it, and adjust BuildMap() below if anything doesn't match on real output.
public static class Petscii
{
    private static readonly byte[] Map = BuildMap();

    private static byte[] BuildMap()
    {
        var map = new byte[128];

        for (int c = 0; c < 128; c++)
            map[c] = (byte)c; // fallback - unmapped codes pass through unchanged

        for (int c = 0x20; c <= 0x3F; c++)
            map[c] = (byte)c; // space, digits, punctuation - same position as ASCII

        for (int c = 'A'; c <= 'Z'; c++)
            map[c] = (byte)(c - 'A' + 1); // uppercase -> screen codes $01-$1A

        for (int c = 'a'; c <= 'z'; c++)
            map[c] = (byte)(c - 'a' + 0x41); // lowercase -> screen codes $41-$5A

        return map;
    }

    public static byte ToScreenCode(char c)
    {
        if (c >= 128)
            throw new ArgumentOutOfRangeException(nameof(c), $"Character '{c}' is outside the 0-127 range.");
        return Map[c];
    }
}
