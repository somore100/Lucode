namespace Lucode;

/// <summary>An error in a Lucode program, always tied to a line number.</summary>
public class LucodeError : Exception
{
    public int Line { get; }

    public LucodeError(int line, string message) : base(message)
    {
        Line = line;
    }

    public override string ToString() => $"Line {Line}: {Message}";
}

/// <summary>A name was read before it had a value. 'when' conditions treat this as "not true yet".</summary>
public sealed class MissingValueError : LucodeError
{
    public string Name { get; }

    public MissingValueError(int line, string name, string message) : base(line, message)
    {
        Name = name;
    }
}

internal static class Suggest
{
    /// <summary>Returns the closest candidate to <paramref name="word"/>, or null if nothing is close.</summary>
    public static string? Closest(string word, IEnumerable<string> candidates)
    {
        string? best = null;
        int bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            int distance = Distance(word.ToLowerInvariant(), candidate.ToLowerInvariant());
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        // Allow roughly one typo per three letters, at least one.
        int allowed = Math.Max(1, word.Length / 3);
        return bestDistance <= allowed ? best : null;
    }

    public static string Hint(string word, IEnumerable<string> candidates)
    {
        var closest = Closest(word, candidates);
        return closest is null ? "" : $" Did you mean '{closest}'?";
    }

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);

                // Swapped neighbouring letters count as one typo.
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }

        return d[a.Length, b.Length];
    }
}
