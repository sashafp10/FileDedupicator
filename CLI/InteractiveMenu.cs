using FilesDeduplicator.Comparers;
using FilesDeduplicator.Interfaces;
using FilesDeduplicator.Services;
using FilesDeduplicator.Utils;

namespace FilesDeduplicator.CLI;

/// <summary>
/// Text-based interactive menu shown when the application is started with no arguments.
/// </summary>
public static class InteractiveMenu
{
    // Last dedup result — available to option 3 without re-scanning
    private static DeduplicationResult? _lastResult;
    private static string?              _lastDir;

    public static void Run()
    {
        PrintBanner();

        while (true)
        {
            PrintMenu();
            var choice = Prompt("Choice").Trim();

            switch (choice)
            {
                case "1": RunDedup();            break;
                case "2": RunMerge();            break;
                case "3": RunReviewAmbiguous();  break;
                case "0": Console.WriteLine("Goodbye."); return;
                default:  Warn("Unknown option, please enter 1, 2, 3, or 0."); break;
            }
        }
    }

    // ── Tool 1 ───────────────────────────────────────────────────────────────

    private static void RunDedup()
    {
        Console.WriteLine();
        Header("Deduplicate files in a directory");

        var dir = PromptDirectory("Directory to deduplicate");
        if (dir is null) return;

        var hash   = PromptHash();
        var dryRun = PromptBool("Dry run (preview only, no files will be moved)", false);

        Console.WriteLine();

        var comparers = BuildComparers(hash);
        var service   = new DeduplicatorService(comparers);

        try
        {
            var result = service.Deduplicate(dir, dryRun);

            // Stash for option 3
            _lastResult = result;
            _lastDir    = dir;

            Console.WriteLine();
            Info($"Scanned      : {result.TotalScanned} files");
            Info($"Groups found : {result.Groups.Count}");
            Info($"Files to move: {result.Moves.Count}");

            if (result.Groups.Count == 0)
            {
                Ok("No duplicates found.");
            }
            else
            {
                // ── Groups summary (console) ─────────────────────────────────
                Console.WriteLine();
                Console.WriteLine(dryRun ? "  [DRY RUN] Duplicate groups:" : "  Duplicate groups:");

                foreach (var group in result.Groups)
                {
                    var keptRel = Path.GetRelativePath(dir, group.KeptPath);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("    KEPT  ");
                    Console.ResetColor();
                    Console.Write(keptRel);
                    if (group.NamingWasAmbiguous)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write("  [ambiguous — chosen by creation date]");
                        Console.ResetColor();
                    }
                    Console.WriteLine();
                    foreach (var dup in group.DuplicatePaths)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"    DUPE  {Path.GetRelativePath(dir, dup)}");
                        Console.ResetColor();
                    }
                    Console.WriteLine();
                }

                // ── Write operation log ──────────────────────────────────────
                WriteLog(result, dir, dryRun);

                // ── HTML report ──────────────────────────────────────────────
                if (PromptBool("Generate HTML report with previews?", true))
                {
                    var reportPath = HtmlReportService.Generate(result, dir);
                    Ok($"Report saved: {reportPath}");
                }

                var ambigCount = result.Groups.Count(g => g.NamingWasAmbiguous);
                if (ambigCount > 0)
                    Info($"{ambigCount} ambiguous group(s) — use option 3 to review and rename.");
            }
        }
        catch (Exception ex)
        {
            Warn($"Error: {ex.Message}");
        }

        Console.WriteLine();
    }

    // ── Tool 3 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Review ambiguous duplicate groups: show kept file and its aliases side-by-side,
    /// then let the user optionally rename the kept file.
    /// </summary>
    private static void RunReviewAmbiguous()
    {
        Console.WriteLine();
        Header("Review ambiguous groups — rename kept files");

        if (_lastResult is null || _lastDir is null)
        {
            Warn("No dedup result in memory. Run option 1 first.");
            Console.WriteLine();
            return;
        }

        var ambiguous = _lastResult.Groups.Where(g => g.NamingWasAmbiguous).ToList();
        if (ambiguous.Count == 0)
        {
            Ok("No ambiguous groups in the last result.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"  {ambiguous.Count} ambiguous group(s). The kept file was chosen by");
        Console.WriteLine(  "  creation date because all names in the group looked equally original.");
        Console.WriteLine(  "  You can rename the kept file now, or press Enter to leave it as-is.");
        Console.WriteLine();

        int idx = 0;
        foreach (var group in ambiguous)
        {
            idx++;
            var keptRel = Path.GetRelativePath(_lastDir, group.KeptPath);
            var aliases = group.DuplicatePaths
                .Select(p => Path.GetRelativePath(_lastDir, p))
                .ToList();

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  [{idx}/{ambiguous.Count}]");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("  KEPT   ");
            Console.ResetColor();
            Console.WriteLine(keptRel);

            foreach (var alias in aliases)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("  ALIAS  ");
                Console.ResetColor();
                Console.WriteLine($"{alias}  (in _duples)");
            }

            var ext = Path.GetExtension(group.KeptPath);
            Console.Write($"  New name{(string.IsNullOrEmpty(ext) ? "" : $" (ext {ext} auto-appended if omitted)")} [Enter = skip]: ");
            var newName = Console.ReadLine()?.Trim();

            if (!string.IsNullOrWhiteSpace(newName))
            {
                if (!string.IsNullOrEmpty(ext) && !newName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    newName += ext;

                if (!File.Exists(group.KeptPath))
                {
                    Warn($"  Kept file no longer exists at: {group.KeptPath}");
                }
                else
                {
                    var newPath = Path.Combine(Path.GetDirectoryName(group.KeptPath)!, newName);
                    if (File.Exists(newPath))
                    {
                        Warn($"  '{newName}' already exists — skipped.");
                    }
                    else
                    {
                        File.Move(group.KeptPath, newPath);
                        // Update the in-memory record so re-running option 3 reflects the rename
                        group.KeptPath = newPath;
                        Ok($"  Renamed → {Path.GetRelativePath(_lastDir, newPath)}");
                    }
                }
            }
            Console.WriteLine();
        }
    }

    // ── Tool 2 ───────────────────────────────────────────────────────────────

    private static void RunMerge()
    {
        Console.WriteLine();
        Header("Merge unique files from infolder → outfolder");

        var inFolder = PromptDirectory("Input folder (source)");
        if (inFolder is null) return;

        var outFolder = PromptPath("Output folder (destination, will be created if absent)");
        if (string.IsNullOrWhiteSpace(outFolder)) return;

        var hash      = PromptHash();
        var dryRun    = PromptBool("Dry run (preview only, no files will be moved)", false);
        var rebuildDb = PromptBool("Rebuild file databases (ignore cached DBs)", false);

        Console.WriteLine();

        var comparers  = BuildComparers(hash);
        var dbService  = new FilesDbService(hash);
        var service    = new MergeService(comparers, dbService);

        try
        {
            var result = service.Merge(inFolder, outFolder, dryRun, rebuildDb,
                msg => Console.WriteLine($"  {msg}"));

            Console.WriteLine();
            Info($"Unique files → outfolder  : {result.UniqueFiles.Count}");
            Info($"Duplicates   → _duples    : {result.DuplicateFiles.Count}");
            Info($"Conflicts (skipped)       : {result.Conflicts.Count}");

            if (dryRun)
            {
                Console.WriteLine();
                Console.WriteLine("  [DRY RUN] Would move unique files:");
                foreach (var (src, dst) in result.UniqueFiles)
                    Console.WriteLine($"    {src}  →  {dst}");

                Console.WriteLine();
                Console.WriteLine("  [DRY RUN] Would move duplicates to _duples:");
                foreach (var (src, dst) in result.DuplicateFiles)
                    Console.WriteLine($"    {src}  →  {dst}");
            }

            if (result.Conflicts.Count > 0)
            {
                Console.WriteLine();
                Warn("Conflicts (skipped — file exists at destination with different content):");
                foreach (var (src, dst) in result.Conflicts)
                    Console.WriteLine($"    {src}  conflicts with  {dst}");
            }
        }
        catch (Exception ex)
        {
            Warn($"Error: {ex.Message}");
        }

        Console.WriteLine();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void WriteLog(DeduplicationResult result, string dir, bool dryRun)
    {
        try
        {
            using var log = OperationLogger.Create(dir, "dedup");

            log.WriteLine($"FilesDeduplicator — Deduplication Log");
            log.WriteLine($"======================================");
            log.WriteLine($"Timestamp   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            log.WriteLine($"Directory   : {dir}");
            log.WriteLine($"Dry run     : {dryRun}");
            log.WriteLine($"Scanned     : {result.TotalScanned} files");
            log.WriteLine($"Groups      : {result.Groups.Count}");
            log.WriteLine($"Duplicates  : {result.Moves.Count}");
            log.WriteLine();

            int idx = 0;
            foreach (var group in result.Groups)
            {
                idx++;
                var ambig = group.NamingWasAmbiguous ? "  [AMBIGUOUS — kept by creation date]" : "";
                log.WriteLine($"Group {idx}{ambig}");
                log.WriteLine($"  KEPT  {Path.GetRelativePath(dir, group.KeptPath)}");
                foreach (var dup in group.DuplicatePaths)
                    log.WriteLine($"  DUPE  {Path.GetRelativePath(dir, dup)}");
                log.WriteLine();
            }

            if (result.Moves.Count > 0)
            {
                log.WriteLine(dryRun ? "[DRY RUN] Would move:" : "Moved:");
                foreach (var (src, dst) in result.Moves)
                    log.WriteLine($"  {src}");
                log.WriteLine($"  → {(dryRun ? "(dry run)" : "done")}");
            }

            Ok($"Log saved   : {log.FilePath}");
        }
        catch (Exception ex)
        {
            Warn($"Could not write log: {ex.Message}");
        }
    }

    private static IReadOnlyList<IFilesComparer> BuildComparers(string hash) =>
        new List<IFilesComparer>
        {
            new HashComparer(hash),
            new ByteByByteComparer()
        };

    private static string? PromptDirectory(string label)
    {
        while (true)
        {
            var path = PromptPath(label);
            if (string.IsNullOrWhiteSpace(path)) return null;

            var full = Path.GetFullPath(path);
            if (Directory.Exists(full))
                return full;

            Warn($"Directory not found: {full}");
            if (!PromptBool("Try again?", true))
                return null;
        }
    }

    private static string PromptHash()
    {
        Console.Write("  Hash algorithm [MD5/SHA1/SHA256] (default MD5): ");
        var input = Console.ReadLine()?.Trim().ToUpperInvariant();
        return input is "SHA1" or "SHA256" ? input : "MD5";
    }

    private static bool PromptBool(string question, bool defaultValue)
    {
        var hint = defaultValue ? "Y/n" : "y/N";
        Console.Write($"  {question} [{hint}]: ");
        var input = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(input)) return defaultValue;
        return input is "y" or "yes";
    }

    private static string Prompt(string label)
    {
        Console.Write($"  {label}: ");
        return Console.ReadLine() ?? string.Empty;
    }

    private static string PromptPath(string label)
    {
        Console.Write($"  {label}: ");
        return Console.ReadLine()?.Trim() ?? string.Empty;
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("  ╔══════════════════════════════════════╗");
        Console.WriteLine("  ║       Files Deduplicator v1.0        ║");
        Console.WriteLine("  ╚══════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintMenu()
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  Select a tool:");
        Console.ResetColor();
        Console.WriteLine("    1  Deduplicate files in a single directory");
        Console.WriteLine("    2  Merge: move unique files from infolder → outfolder");

        // Dim option 3 if there is no result in memory yet
        if (_lastResult?.Groups.Any(g => g.NamingWasAmbiguous) == true)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"    3  Review ambiguous groups ({_lastResult.Groups.Count(g => g.NamingWasAmbiguous)} pending)");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("    3  Review ambiguous groups  (run option 1 first)");
            Console.ResetColor();
        }

        Console.WriteLine("    0  Exit");
        Console.WriteLine();
    }

    private static void Header(string text)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  ── {text} ──");
        Console.ResetColor();
    }

    private static void Info(string text)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("  ✓ ");
        Console.ResetColor();
        Console.WriteLine(text);
    }

    private static void Ok(string text)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  {text}");
        Console.ResetColor();
    }

    private static void Warn(string text)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  ! {text}");
        Console.ResetColor();
    }
}
