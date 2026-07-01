using FilesDeduplicator.Interfaces;
using FilesDeduplicator.Utils;

namespace FilesDeduplicator.Services;

/// <summary>Describes a set of identical files found during deduplication.</summary>
public sealed class DuplicateGroup
{
    /// <summary>The file chosen to keep (most "original" name, oldest on tie).</summary>
    public string KeptPath { get; set; } = string.Empty;

    /// <summary>Copy-name score of the kept file (0 = looks original).</summary>
    public int KeptScore { get; set; }

    /// <summary>Paths of all duplicate files scheduled to move to _duples.</summary>
    public List<string> DuplicatePaths { get; } = new();

    /// <summary>
    /// True when the kept file and at least one duplicate had the same copy-name score,
    /// meaning the choice was made by creation-date / alphabetical fallback.
    /// </summary>
    public bool NamingWasAmbiguous { get; set; }
}

public sealed class DeduplicationResult
{
    public List<(string Source, string Destination)> Moves { get; } = new();
    public List<DuplicateGroup> Groups { get; } = new();
    public int TotalScanned { get; set; }
    /// <summary>Files excluded from comparison because their extension was not in the active filter.</summary>
    public int SkippedByFilter { get; set; }
}

/// <summary>
/// Finds and moves duplicate files within a single directory to &lt;directory&gt;/_duples.
///
/// Algorithm:
///   1. Group files by exact byte-size (cheap, no I/O beyond stat).
///   2. Within each size group build connected components via union-find,
///      using the IFilesComparer pipeline as the equality predicate.
///   3. For each component (>=2 files) pick the "keeper" via copy-name scoring
///      then creation-date then shortest name then alphabetical order.
///   4. Move all non-keepers to _duples preserving the original folder structure.
/// </summary>
public sealed class DeduplicatorService
{
    private const string DuplesFolder = "_duples";

    private readonly IReadOnlyList<IFilesComparer> _comparers;
    private readonly FileTypeFilter _filter;

    public DeduplicatorService(IReadOnlyList<IFilesComparer> comparers, FileTypeFilter? filter = null)
    {
        _comparers = comparers;
        _filter    = filter ?? FileTypeFilter.PassAll;
    }

    public DeduplicationResult Deduplicate(string directory, bool dryRun)
    {
        directory = Path.GetFullPath(directory);
        var result = new DeduplicationResult();

        var allFiles = Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(f => !IsUnderDuples(f, directory))
            .ToList();

        result.TotalScanned = allFiles.Count;

        // Partition: files that pass the filter go into dedup; others are simply left alone
        var filteredFiles = allFiles.Where(f => _filter.Includes(f)).ToList();
        result.SkippedByFilter = allFiles.Count - filteredFiles.Count;

        // Only size-groups with >1 member are candidates
        var sizeGroups = filteredFiles
            .GroupBy(f => new FileInfo(f).Length)
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList())
            .ToList();

        int candidateCount = sizeGroups.Sum(g => g.Count);

        // ── Phase 1: compare ─────────────────────────────────────────────────
        Console.WriteLine($"  Files to scan         : {result.TotalScanned}");
        if (!_filter.IsPassAll)
            Console.WriteLine($"  Filter (extensions)   : {_filter}");
        Console.WriteLine($"  Filtered candidates   : {filteredFiles.Count}");
        Console.WriteLine($"  Size-group candidates : {candidateCount}");
        Console.WriteLine();

        using (var progress = new ProgressReporter(candidateCount, "Comparing"))
        {
            foreach (var group in sizeGroups)
            {
                var dupGroups = FindDuplicateGroups(group, progress);

                foreach (var component in dupGroups)
                {
                    var keeper    = PickKeeper(component);
                    var keptScore = CopyNameScorer.Score(Path.GetFileName(keeper));
                    var dg        = new DuplicateGroup { KeptPath = keeper, KeptScore = keptScore };

                    foreach (var dup in component.Where(f => f != keeper))
                    {
                        int dupScore = CopyNameScorer.Score(Path.GetFileName(dup));
                        if (dupScore == keptScore)
                            dg.NamingWasAmbiguous = true;

                        dg.DuplicatePaths.Add(dup);

                        var relative = Path.GetRelativePath(directory, dup);
                        var dest     = Path.Combine(directory, DuplesFolder, relative);
                        result.Moves.Add((dup, dest));
                    }

                    result.Groups.Add(dg);
                }
            }
        } // progress line cleared on dispose

        // ── Phase 2: move ────────────────────────────────────────────────────
        if (!dryRun && result.Moves.Count > 0)
        {
            Console.WriteLine();
            using var moveProgress = new ProgressReporter(result.Moves.Count, "Moving");

            foreach (var (src, dst) in result.Moves)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Move(src, dst);
                moveProgress.Advance(
                    movedFile: $"{Path.GetRelativePath(directory, src)}  →  _duples{Path.DirectorySeparatorChar}{Path.GetRelativePath(directory, src)}");
            }
        }

        return result;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private List<List<string>> FindDuplicateGroups(List<string> files, ProgressReporter progress)
    {
        int n      = files.Count;
        var parent = Enumerable.Range(0, n).ToArray();

        for (int i = 0; i < n; i++)
        {
            progress.Advance();
            for (int j = i + 1; j < n; j++)
                if (FilesAreEqual(files[i], files[j]))
                    Union(parent, i, j);
        }

        return files
            .Select((f, idx) => (f, root: Find(parent, idx)))
            .GroupBy(x => x.root)
            .Where(g => g.Count() > 1)
            .Select(g => g.Select(x => x.f).ToList())
            .ToList();
    }

    private static string PickKeeper(List<string> group) =>
        group
            .OrderBy(f => CopyNameScorer.Score(Path.GetFileName(f)))
            .ThenBy(f  => new FileInfo(f).CreationTimeUtc)
            .ThenBy(f  => Path.GetFileName(f).Length)
            .ThenBy(f  => f, StringComparer.OrdinalIgnoreCase)
            .First();

    private bool FilesAreEqual(string p1, string p2)
    {
        foreach (var c in _comparers)
            if (c.EqualsIndex(p1, p2) == 0)
                return false;
        return true;
    }

    private static int Find(int[] parent, int i)
    {
        if (parent[i] != i)
            parent[i] = Find(parent, parent[i]);
        return parent[i];
    }

    private static void Union(int[] parent, int a, int b) =>
        parent[Find(parent, a)] = Find(parent, b);

    private static bool IsUnderDuples(string filePath, string root)
    {
        var relative = Path.GetRelativePath(root, filePath);
        return relative
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(p => p.Equals(DuplesFolder, StringComparison.OrdinalIgnoreCase));
    }
}
