# IF-Text-Compactor

Text-adventure string compressor for 6502 / VIC-20 targets. Builds a shared
substitution dictionary (up to 128 entries, frequency-driven, BPE-style) from
a corpus of location and object names/descriptions, then emits a
KickAssembler `.asm` source file containing:

- the dictionary strings
- compressed location name strings
- compressed location description strings
- compressed object name strings
- compressed object description strings
- a lo/hi address lookup table for each of the five blocks above

Every string is null-terminated. Bytes with bit 7 set (`$80` upward) are
dictionary references; everything else is a literal character.

## Build

```
cd IF-Text-Compactor
dotnet build
```

If `dotnet --version` on this machine reports something other than 8.x,
edit `<TargetFramework>` in `IF-Text-Compactor.csproj` to match (e.g. `net9.0`) - the
rest of the project doesn't depend on the exact version.

Release builds (`dotnet publish -c Release`) produce a single framework-dependent
`IFTC.exe` at the project root - see the `csproj` for details.

## Usage

```
IFTC <inputfile> [--start $1000] [--out file.asm] [--charset A|P]
```

- `<inputfile>` - required. See format below.
- `--start <hex>` - 16-bit start address, e.g. `$1000` or `1000`. Default `$1000`.
- `--out <path>` - output `.asm` path. Default: `<inputfile>.asm`.
- `--charset A|P` - `A` (default) emits plain ASCII byte values for literal
  characters; your print routine is responsible for any PETSCII translation.
  `P` bakes VIC-20 PETSCII screen codes into the literal bytes directly (see
  the caveat below).

Try it against the bundled sample:

```
dotnet run -- sample_input.txt --start $2000 --out sample_output.asm
```

## Input file format

```
LOC:<location name>
<location description, one line>

OBJ:<object name>
<object description, one line>
```

- Tags are case-insensitive (`loc:` / `LOC:` both work).
- Each tagged name line must be followed by exactly one description line -
  the next non-blank line.
- Entries are separated by one or more blank lines.
- `sample_input.txt` (the Zork transcript extraction) is a working example
  of the format.

## Character set

Only characters in the 0-127 range are supported, matching the bit-7
dictionary-flag scheme. Curly quotes, em/en dashes, ellipsis characters and
non-breaking spaces are normalised to their plain-ASCII equivalents
automatically. Anything else outside 0-127 causes a parse error naming the
offending entry and character, rather than silently mangling text - edit the
source or extend `TextNormalizer.Substitutions` if you hit one regularly.

## PETSCII caveat

The `--charset P` mapping in `Petscii.cs` targets the VIC-20/C64
"upper/lowercase" character-set screen codes (needed for mixed-case text).
This has **not** been verified against real hardware or a VICE screen-code
reference - check a handful of encoded bytes against your own chargen bank
and print routine before trusting it wholesale, and adjust `BuildMap()` if
anything's off.

## Dictionary construction

Merges are chosen purely by occurrence frequency each round (the standard
BPE heuristic), because every merge always saves exactly one byte per
occurrence in the compressed stream regardless of the merged string's
length - so frequency alone predicts stream savings correctly. The loop
stops on its own once the best remaining candidate's frequency no longer
exceeds `mergedLength + 1` (the one-off cost of storing it in the
dictionary), or once 128 entries are reached - whichever comes first. No
tuning knob needed.

Dictionary entries are stored flat (literal characters only) even though
some are built by merging earlier merges - the runtime decoder never needs
to recurse.

## Stats

Every run prints:

- input file size (bytes)
- uncompressed memory requirement - flat null-terminated strings with no
  dictionary substitution, plus the four index tables you'd need either way
  for direct indexed lookup
- compressed memory requirement - the dictionary plus all five compressed
  string blocks plus all five index tables (i.e. everything the `.asm` file
  actually contains)

Both totals include the lookup-table overhead so the comparison isolates
the effect of dictionary compression rather than conflating it with
structure you'd need regardless.
