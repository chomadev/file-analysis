namespace FileAnalysis.Infrastructure.Scanning;

/// <summary>Simple glob matcher supporting * and ? wildcards.</summary>
public static class GlobMatcher
{
    /// <summary>Returns true if <paramref name="name"/> matches <paramref name="pattern"/>.</summary>
    public static bool Matches(string pattern, string name) =>
        MatchesCore(pattern.AsSpan(), name.AsSpan());

    private static bool MatchesCore(ReadOnlySpan<char> pattern, ReadOnlySpan<char> name)
    {
        while (true)
        {
            if (pattern.IsEmpty) return name.IsEmpty;
            if (pattern[0] == '*')
            {
                pattern = pattern[1..];
                if (pattern.IsEmpty) return true;
                for (var i = 0; i <= name.Length; i++)
                    if (MatchesCore(pattern, name[i..]))
                        return true;
                return false;
            }
            if (name.IsEmpty) return false;
            if (pattern[0] != '?' && char.ToUpperInvariant(pattern[0]) != char.ToUpperInvariant(name[0]))
                return false;
            pattern = pattern[1..];
            name = name[1..];
        }
    }
}
