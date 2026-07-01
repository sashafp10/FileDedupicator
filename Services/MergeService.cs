using FilesDeduplicator.Interfaces;
using FilesDeduplicator.Models;
using FilesDeduplicator.Utils;

namespace FilesDeduplicator.Services;

public sealed class MergeResult
{
    /// <summary>Files from infolder that are unique and will be moved to outfolder.</summary>
    public List<(string Source, string Destination)> UniqueFiles { get; } = new();

    /// <summary>Files from infolder that already exist in outfolder and will be moved to _duples.</summary>
    public List<(string Source, string Destination)> DuplicateFiles { get; } = new();

    /// <summary>Files that could not be moved due to path conflicts in outfolder.</summary>
    public List<(string Source, string ConflictingTarget)> Conflicts { get; } = new();

    /// <summary>Files skipped by the extension filter — copied to outfolder as-is without dedup check.</summary>
    public List<(string Source, string Destination)> PassthroughFiles { get; } = new();
}

/// <summary>
/// Merges an infolder into an outfolder:
///   - Files unique to infolder (not present in outfolder) → moved to outfolder maintaining structure.
///   - Files in infolder that are duplicates of files in outfolder → moved to infolder/_duples.
///
/// Comparison pipeline:
///   1. Pre-filter by file size (using JSON DBs).
///   2. Pre-filter by hash stored in the DB.
///   3. Final confirmation via the IFilesComparer pipeline (byte-by-byte by default).
/// </summary>
public sealed class MergeService
{
    private const string DuplesFolder = "_duples";

    private readonly IReadOnlyList<IFilesComparer> _comparers;
    private readonly FilesDbService _dbService;
    private readonly FileTypeFilter _filter;

    public MergeService(IReadOnlyList<IFilesComparer> comparers, FilesDbService dbService, FileTypeFilter? filter = null)
    {
        _comparers = comparers;
        _dbService = dbService;
        _filter    = filter ?? FileTypeFilter.PassAll;
    }

    public MergeResult Merge(
        string inFolder,
        string outFolder,
        bool dryRun,
        bool rebuildDb,
        Action<string>? onProgress = null)
    {
        inFolder  = Path.GetFullPath(inFolder);
        outFolder = Path.GetFullPath(outFolder);

        ValidatePaths(inFolder, outFolder);

        if (!Directory.Exists(outFolder))
            Directory.CreateDirectory(outFolder);

        var result = new MergeResult();

        // ── Build / load databases ───────────────────────────────────────────
        var inDb  = LoadOrBuild(inFolder,  rebuildDb, "input",  onProgress);
        var outDb = LoadOrBuild(outFolder, rebuildDb, "output", onProgress);

        // ── Index outDb by size → list of records ────────────────────────────
        var outBySize = outDb.Files
            .GroupBy(r => r.SizeBytes)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ── Process each infolder file ───────────────────────────────────────
        var inFiles = inDb.Files
            .Where(r => !IsUnderDuples(r.RelativePath))
            .ToList();

        using (var compareProgress = new ProgressReporter(inFiles.Count, "Comparing"))
        foreach (var inRecord in inFiles)
        {
            var srcPath = Path.Combine(inFolder, inRecord.RelativePath);

            compareProgress.Advance();

            if (!File.Exists(srcPath))
                continue; // file disappeared since DB was built

            // Files excluded by the filter are copied to outfolder as-is (no dedup check)
            if (!_filter.Includes(srcPath))
            {
                var passthroughDst = Path.Combine(outFolder, inRecord.RelativePath);
                result.PassthroughFiles.Add((srcPath, passthroughDst));
                continue;
            }

            if (!outBySize.TryGetValue(inRecord.SizeBytes, out var candidates))
            {
                // No size match → unique file, move to outfolder
                var dst = Path.Combine(outFolder, inRecord.RelativePath);
                result.UniqueFiles.Add((srcPath, dst));
                continue;
            }

            // Filter candidates by hash (same hash type AND value)
            var hashMatches = candidates.Where(c =>
                string.Equals(c.HashType,  inRecord.HashType,  StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.HashValue, inRecord.HashValue, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            bool isDuplicate = false;
            foreach (var candidate in hashMatches)
            {
                var candidatePath = Path.Combine(outFolder, candidate.RelativePath);
                if (!File.Exists(candidatePath))
                    continue;

                // Final byte-by-byte confirmation via comparer pipeline
                if (FilesAreEqual(srcPath, candidatePath))
                {
                    isDuplicate = true;
                    onProgress?.Invoke($"  Duplicate → _duples: {inRecord.RelativePath}");
                    break;
                }
            }

            if (isDuplicate)
            {
                var duplesDst = Path.Combine(inFolder, DuplesFolder, inRecord.RelativePath);
                result.DuplicateFiles.Add((srcPath, duplesDst));
            }
            else
            {
                var dst = Path.Combine(outFolder, inRecord.RelativePath);

                // Conflict check: a file with the same path but different content exists in outfolder
                if (File.Exists(dst))
                {
                    result.Conflicts.Add((srcPath, dst));
                    onProgress?.Invoke($"  Conflict (same path, different content): {inRecord.RelativePath}");
                    continue;
                }

                result.UniqueFiles.Add((srcPath, dst));
            }
        }

        // ── Apply moves ──────────────────────────────────────────────────────
        if (!dryRun)
        {
            int totalMoves = result.DuplicateFiles.Count + result.UniqueFiles.Count + result.PassthroughFiles.Count;
            if (totalMoves > 0)
            {
                Console.WriteLine();
                using var moveProgress = new ProgressReporter(totalMoves, "Moving");

                foreach (var (src, dst) in result.DuplicateFiles)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Move(src, dst);
                    moveProgress.Advance(movedFile: $"{Path.GetRelativePath(inFolder, src)}  →  _duples");
                }

                foreach (var (src, dst) in result.UniqueFiles)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Move(src, dst);
                    moveProgress.Advance(movedFile: $"{Path.GetRelativePath(inFolder, src)}  →  outfolder");
                }

                foreach (var (src, dst) in result.PassthroughFiles)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(src, dst, overwrite: false);
                    moveProgress.Advance(movedFile: $"{Path.GetRelativePath(inFolder, src)}  →  passthrough");
                }
            }
        }

        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private FilesDb LoadOrBuild(string folder, bool forceRebuild, string label, Action<string>? onProgress)
    {
        var dbPath = _dbService.GetDbPath(folder);
        FilesDb? db = null;

        if (!forceRebuild)
            db = _dbService.LoadDb(dbPath);

        if (db is null)
        {
            onProgress?.Invoke($"Building {label} DB...");
            db = _dbService.BuildDb(folder, f => onProgress?.Invoke($"    Indexing: {f}"));
        }
        else
        {
            onProgress?.Invoke($"Loaded existing {label} DB ({db.Files.Count} files).");
        }

        _dbService.SaveDb(db, dbPath);
        onProgress?.Invoke($"  {db.Files.Count} files → {dbPath}");
        return db;
    }

    private bool FilesAreEqual(string path1, string path2)
    {
        foreach (var comparer in _comparers)
        {
            if (comparer.EqualsIndex(path1, path2) == 0)
                return false;
        }
        return true;
    }

    private static bool IsUnderDuples(string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p.Equals(DuplesFolder, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidatePaths(string inFolder, string outFolder)
    {
        if (string.Equals(inFolder, outFolder, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("infolder and outfolder must be different directories.");

        // Prevent outfolder from being inside infolder or vice versa
        var sep = Path.DirectorySeparatorChar;
        var inNorm  = inFolder.TrimEnd(sep)  + sep;
        var outNorm = outFolder.TrimEnd(sep) + sep;

        if (outNorm.StartsWith(inNorm, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("outfolder must not be a subdirectory of infolder.");

        if (inNorm.StartsWith(outNorm, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("infolder must not be a subdirectory of outfolder.");
    }
}
