using System.Text.RegularExpressions;

namespace FilesDeduplicator.Utils;

/// <summary>
/// Scores a filename by how "copy-like" it looks.
/// 0 = looks like an original. Higher value = more likely a generated copy name.
/// Rules are checked against the filename stem (no extension).
/// </summary>
public static class CopyNameScorer
{
    private static readonly (Regex Pattern, int Score)[] Rules =
    {
        // "Copy of filename.ext"
        (new Regex(@"^copy of ", RegexOptions.IgnoreCase | RegexOptions.Compiled), 10),

        // "filename (copy).ext"  or  "filename(copy).ext"
        (new Regex(@"\s*\(copy\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled), 8),

        // "filename - Copy.ext"  or  "filename - Copy (2).ext"
        (new Regex(@"\s*-\s*copy(\s*\(\d+\))?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled), 8),

        // "filename_copy.ext"  or  "filename copy.ext"
        (new Regex(@"[_ ]copy\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled), 7),

        // "filename (2).ext"  or  "filename(2).ext"
        (new Regex(@"\s*\(\d+\)\s*$", RegexOptions.Compiled), 5),

        // "filename_1.ext"  "filename_2.ext" etc.
        (new Regex(@"_\d+$", RegexOptions.Compiled), 4),

        // "filename 1.ext"  (trailing space + digit)
        (new Regex(@"\s\d+$", RegexOptions.Compiled), 3),
    };

    /// <summary>Returns the copy-likeness score for the given filename (path-safe).</summary>
    public static int Score(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        foreach (var (pattern, score) in Rules)
            if (pattern.IsMatch(stem))
                return score;
        return 0;
    }
}
