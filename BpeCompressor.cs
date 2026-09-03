namespace IFTextCompactor;

// Builds a shared substitution dictionary (max 128 entries, addressed via bit 7
// of the output byte) from the whole corpus, using a frequency-driven merge
// pass similar to byte-pair encoding, then encodes every name/description
// string against it.
//
// Design notes:
//  - Every merge step always saves exactly 1 output byte per occurrence
//    (two adjacent tokens become one), regardless of the merged string's
//    length. So plain occurrence-count is the right per-round selection
//    metric - it directly predicts stream savings.
//  - A merge is only worth keeping if its one-off dictionary storage cost
//    (mergedLength + 1 bytes, including the null terminator) is smaller
//    than the total bytes it saves across the corpus. That gives a natural,
//    parameter-free stopping rule: stop once the best remaining candidate's
//    occurrence count no longer clears mergedLength + 1.
//  - Dictionary entries are stored FLAT (literal characters only, no nested
//    dictionary references) even though the merges that built them are
//    hierarchical - this keeps the runtime decoder a single indirection
//    with no recursion.
public static class BpeCompressor
{
    private const int MaxDictionaryEntries = 128;

    public static CompressionResult Compress(List<Entry> entries, Charset charset)
    {
        var locations = entries.Where(e => e.Kind == EntryKind.Location).ToList();
        var objects = entries.Where(e => e.Kind == EntryKind.Object).ToList();

        var locNameTokens = locations.Select(e => ToTokens(e.Name)).ToList();
        var locDescTokens = locations.Select(e => ToTokens(e.Description)).ToList();
        var objNameTokens = objects.Select(e => ToTokens(e.Name)).ToList();
        var objDescTokens = objects.Select(e => ToTokens(e.Description)).ToList();

        var allTokenLists = new List<List<Token>>();
        allTokenLists.AddRange(locNameTokens);
        allTokenLists.AddRange(locDescTokens);
        allTokenLists.AddRange(objNameTokens);
        allTokenLists.AddRange(objDescTokens);

        List<string> dictionary = BuildDictionary(allTokenLists);

        var dictIndex = new Dictionary<string, int>();
        for (int i = 0; i < dictionary.Count; i++)
            dictIndex[dictionary[i]] = i;

        var dictionaryEncoded = dictionary
            .Select(flat => new CompressedString
            {
                Original = flat,
                Bytes = EncodeFlatLiteral(flat, charset)
            })
            .ToList();

        return new CompressionResult
        {
            Dictionary = dictionaryEncoded,
            LocationNames = Encode(locations.Select(e => e.Name).ToList(), locNameTokens, dictIndex, charset),
            LocationDescriptions = Encode(locations.Select(e => e.Description).ToList(), locDescTokens, dictIndex, charset),
            ObjectNames = Encode(objects.Select(e => e.Name).ToList(), objNameTokens, dictIndex, charset),
            ObjectDescriptions = Encode(objects.Select(e => e.Description).ToList(), objDescTokens, dictIndex, charset),
        };
    }

    public static byte EncodeLiteral(char c, Charset charset) =>
        charset == Charset.Petscii ? Petscii.ToScreenCode(c) : (byte)c;

    private static byte[] EncodeFlatLiteral(string flat, Charset charset)
    {
        var bytes = new byte[flat.Length + 1];
        for (int i = 0; i < flat.Length; i++)
            bytes[i] = EncodeLiteral(flat[i], charset);
        bytes[flat.Length] = 0x00;
        return bytes;
    }

    private static List<Token> ToTokens(string s)
    {
        var list = new List<Token>(s.Length);
        foreach (char c in s)
            list.Add(new Token(c.ToString()));
        return list;
    }

    private static List<string> BuildDictionary(List<List<Token>> allTokenLists)
    {
        var dictionary = new List<string>();

        while (dictionary.Count < MaxDictionaryEntries)
        {
            var counts = new Dictionary<(string Left, string Right), int>();
            var order = new List<(string Left, string Right)>();

            foreach (var tokens in allTokenLists)
            {
                for (int i = 0; i + 1 < tokens.Count; i++)
                {
                    var key = (tokens[i].Flat, tokens[i + 1].Flat);
                    if (counts.TryGetValue(key, out int c))
                    {
                        counts[key] = c + 1;
                    }
                    else
                    {
                        counts[key] = 1;
                        order.Add(key);
                    }
                }
            }

            if (counts.Count == 0)
                break;

            (string Left, string Right) best = order[0];
            int bestCount = counts[best];

            foreach (var key in order)
            {
                int c = counts[key];
                if (c > bestCount)
                {
                    bestCount = c;
                    best = key;
                }
            }

            int mergedLength = best.Left.Length + best.Right.Length;

            if (bestCount <= mergedLength + 1)
                break; // no candidate left that would actually shrink total output

            string mergedFlat = best.Left + best.Right;
            dictionary.Add(mergedFlat);

            foreach (var tokens in allTokenLists)
                MergeInPlace(tokens, best.Left, best.Right, mergedFlat);
        }

        return dictionary;
    }

    private static void MergeInPlace(List<Token> tokens, string left, string right, string mergedFlat)
    {
        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            if (tokens[i].Flat == left && tokens[i + 1].Flat == right)
            {
                tokens[i] = new Token(mergedFlat);
                tokens.RemoveAt(i + 1);
                // Intentionally do not step back - this is a single left-to-right,
                // non-overlapping merge pass, standard for BPE-style construction.
            }
        }
    }

    private static List<CompressedString> Encode(
        List<string> originals,
        List<List<Token>> tokenLists,
        Dictionary<string, int> dictIndex,
        Charset charset)
    {
        var result = new List<CompressedString>(tokenLists.Count);

        for (int i = 0; i < tokenLists.Count; i++)
        {
            var bytes = new List<byte>();

            foreach (var token in tokenLists[i])
            {
                if (token.Flat.Length == 1)
                {
                    bytes.Add(EncodeLiteral(token.Flat[0], charset));
                }
                else
                {
                    int idx = dictIndex[token.Flat];
                    bytes.Add((byte)(0x80 | idx));
                }
            }

            bytes.Add(0x00);
            result.Add(new CompressedString { Original = originals[i], Bytes = bytes.ToArray() });
        }

        return result;
    }

    private sealed class Token
    {
        public string Flat { get; }
        public Token(string flat) => Flat = flat;
    }
}
