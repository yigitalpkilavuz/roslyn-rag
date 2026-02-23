using RoslynRag.Core.Interfaces;

namespace RoslynRag.Parsing;

/// <summary>
/// cAST-style chunk splitter. Currently uses syntax-aware line splitting as a fallback.
/// TODO: Integrate tree-sitter-c-sharp native library for true AST-based split-then-merge.
/// The algorithm should:
///   1. Parse the chunk via tree-sitter-c-sharp
///   2. Recursively split large AST nodes into sub-nodes
///   3. Merge small sibling nodes to fill the token budget
///   4. Respect syntactic boundaries (never split mid-statement)
/// </summary>
public sealed class TreeSitterChunkSplitter : IChunkSplitter
{
    public int MaxChunkChars { get; }

    public TreeSitterChunkSplitter(int maxChunkChars = 4000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxChunkChars, 0);
        MaxChunkChars = maxChunkChars;
    }

    public IReadOnlyList<string> SplitIfNeeded(string sourceText)
    {
        if (sourceText.Length <= MaxChunkChars)
            return [sourceText];

        return SplitBySyntaxBoundaries(sourceText);
    }

    /// <summary>
    /// Splits code at syntax-aware boundaries (blank lines, closing braces, statement ends).
    /// This is a heuristic fallback until tree-sitter native integration is complete.
    /// </summary>
    private List<string> SplitBySyntaxBoundaries(string sourceText)
    {
        var lines = sourceText.Split('\n');
        var chunks = new List<string>();
        var currentChunk = new List<string>();
        var currentLength = 0;

        // Extract preamble (// File:, // Namespace:, etc.) to prepend to each part
        var preambleLines = new List<string>();
        var contentStartIndex = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("//"))
            {
                preambleLines.Add(lines[i]);
                contentStartIndex = i + 1;
            }
            else if (string.IsNullOrWhiteSpace(lines[i]) && preambleLines.Count > 0)
            {
                contentStartIndex = i + 1;
                break;
            }
            else
            {
                break;
            }
        }

        var preamble = preambleLines.Count > 0
            ? string.Join('\n', preambleLines) + "\n\n"
            : string.Empty;

        var preambleLength = preamble.Length;
        var effectiveMax = MaxChunkChars - preambleLength;

        for (var i = contentStartIndex; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineLength = line.Length + 1;

            if (currentLength + lineLength > effectiveMax && currentChunk.Count > 0)
            {
                var splitIndex = FindSplitPoint(currentChunk);

                if (splitIndex > 0 && splitIndex < currentChunk.Count - 1)
                {
                    var firstPart = currentChunk.Take(splitIndex + 1).ToList();
                    var remainder = currentChunk.Skip(splitIndex + 1).ToList();

                    chunks.Add(preamble + string.Join('\n', firstPart));

                    currentChunk = remainder;
                    currentLength = remainder.Sum(l => l.Length + 1);
                }
                else
                {
                    chunks.Add(preamble + string.Join('\n', currentChunk));
                    currentChunk = [];
                    currentLength = 0;
                }
            }

            currentChunk.Add(line);
            currentLength += lineLength;
        }

        if (currentChunk.Count > 0)
        {
            chunks.Add(preamble + string.Join('\n', currentChunk));
        }

        return chunks;
    }

    /// <summary>
    /// Finds the best line index to split at, preferring (in order):
    /// 1. Blank lines
    /// 2. Lines with closing braces (end of blocks)
    /// 3. Lines ending with semicolons (end of statements)
    /// Searches from the middle outward to produce balanced splits.
    /// </summary>
    private static int FindSplitPoint(List<string> lines)
    {
        var midpoint = lines.Count / 2;
        var bestIndex = -1;
        var bestPriority = int.MaxValue;
        var bestDistance = int.MaxValue;

        for (var i = 1; i < lines.Count - 1; i++)
        {
            var trimmed = lines[i].Trim();
            var priority = GetSplitPriority(trimmed);

            if (priority < 0) continue;

            var distance = Math.Abs(i - midpoint);

            if (priority < bestPriority || (priority == bestPriority && distance < bestDistance))
            {
                bestPriority = priority;
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static int GetSplitPriority(string trimmedLine)
    {
        if (string.IsNullOrEmpty(trimmedLine)) return 0;       // Blank line — best
        if (trimmedLine == "}") return 1;                       // Closing brace
        if (trimmedLine.EndsWith(";")) return 2;                // Statement end
        if (trimmedLine.EndsWith("{")) return 3;                // Opening brace
        return -1;                                               // Not a good split point
    }
}
