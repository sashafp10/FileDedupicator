using System.CommandLine;
using FilesDeduplicator.CLI;
using FilesDeduplicator.Comparers;
using FilesDeduplicator.Interfaces;
using FilesDeduplicator.Services;

// ── If no arguments: show interactive menu ──────────────────────────────────
if (args.Length == 0)
{
    InteractiveMenu.Run();
    return 0;
}

// ── CLI mode ────────────────────────────────────────────────────────────────
var rootCommand = new RootCommand("FilesDeduplicator — find and manage duplicate files across directories");

// ── Shared options ───────────────────────────────────────────────────────────
var hashOption = new Option<string>(
    name: "--hash",
    getDefaultValue: () => "MD5",
    description: "Hash algorithm to use for comparison (MD5, SHA1, SHA256)");

var dryRunOption = new Option<bool>(
    name: "--dry-run",
    description: "Preview changes without moving any files");

// ── dedup command ─────────────────────────────────────────────────────────────
var dedupCommand = new Command("dedup", "Remove duplicate files within a single directory. Duplicates are moved to <directory>/_duples.");
var dedupDirArg = new Argument<string>("directory", "Directory to scan and deduplicate");
var reportOption = new Option<bool>(
    name: "--report",
    description: "Generate an HTML report with file previews after scanning");
dedupCommand.AddArgument(dedupDirArg);
dedupCommand.AddOption(hashOption);
dedupCommand.AddOption(dryRunOption);
dedupCommand.AddOption(reportOption);

dedupCommand.SetHandler((string dir, string hash, bool dryRun, bool report) =>
{
    var fullDir = Path.GetFullPath(dir);
    if (!Directory.Exists(fullDir))
    {
        Console.Error.WriteLine($"Directory not found: {fullDir}");
        Environment.Exit(1);
    }

    Console.WriteLine($"Deduplicating: {fullDir}");
    Console.WriteLine($"Hash         : {hash.ToUpperInvariant()}");
    Console.WriteLine($"Dry run      : {dryRun}");
    Console.WriteLine();

    var comparers = BuildComparers(hash);
    var service   = new DeduplicatorService(comparers);
    var result = service.Deduplicate(fullDir, dryRun);

    Console.WriteLine();
    Console.WriteLine($"Scanned : {result.TotalScanned} files");
    Console.WriteLine($"Groups  : {result.Groups.Count}");
    Console.WriteLine($"Dupes   : {result.Moves.Count}");

    if (result.Groups.Count > 0)
    {
        Console.WriteLine();
        foreach (var group in result.Groups)
        {
            var keptRel = Path.GetRelativePath(fullDir, group.KeptPath);
            var ambMark = group.NamingWasAmbiguous ? "  [ambiguous — chosen by creation date]" : "";
            Console.WriteLine($"  KEPT  {keptRel}{ambMark}");
            foreach (var dup in group.DuplicatePaths)
                Console.WriteLine($"  DUPE  {Path.GetRelativePath(fullDir, dup)}");
            Console.WriteLine();
        }
    }

    if (result.Moves.Count > 0 && dryRun)
    {
        Console.WriteLine("[DRY RUN] Would move:");
        foreach (var (src, dst) in result.Moves)
            Console.WriteLine($"  {src}  →  {dst}");
    }

    if (report)
    {
        var reportPath = HtmlReportService.Generate(result, fullDir);
        Console.WriteLine();
        Console.WriteLine($"Report: {reportPath}");
    }
}, dedupDirArg, hashOption, dryRunOption, reportOption);

// ── merge command ─────────────────────────────────────────────────────────────
var mergeCommand = new Command(
    "merge",
    "Move files unique to <infolder> into <outfolder>. " +
    "Files already present in <outfolder> are moved to <infolder>/_duples. " +
    "Folder structure is preserved in both cases.");

var inFolderArg  = new Argument<string>("infolder",  "Source folder containing files to process");
var outFolderArg = new Argument<string>("outfolder", "Destination folder (created if absent)");
var rebuildDbOption = new Option<bool>(
    name: "--rebuild-db",
    description: "Force rebuild of file databases (ignore any cached DB JSON files)");

mergeCommand.AddArgument(inFolderArg);
mergeCommand.AddArgument(outFolderArg);
mergeCommand.AddOption(hashOption);
mergeCommand.AddOption(dryRunOption);
mergeCommand.AddOption(rebuildDbOption);

mergeCommand.SetHandler((string inFolder, string outFolder, string hash, bool dryRun, bool rebuildDb) =>
{
    var fullIn  = Path.GetFullPath(inFolder);
    var fullOut = Path.GetFullPath(outFolder);

    if (!Directory.Exists(fullIn))
    {
        Console.Error.WriteLine($"infolder not found: {fullIn}");
        Environment.Exit(1);
    }

    Console.WriteLine($"Input folder : {fullIn}");
    Console.WriteLine($"Output folder: {fullOut}");
    Console.WriteLine($"Hash         : {hash.ToUpperInvariant()}");
    Console.WriteLine($"Dry run      : {dryRun}");
    Console.WriteLine($"Rebuild DB   : {rebuildDb}");
    Console.WriteLine();

    var comparers = BuildComparers(hash);
    var dbService = new FilesDbService(hash);
    var service   = new MergeService(comparers, dbService);

    try
    {
        var result = service.Merge(fullIn, fullOut, dryRun, rebuildDb, Console.WriteLine);

        Console.WriteLine();
        Console.WriteLine($"Unique files → outfolder : {result.UniqueFiles.Count}");
        Console.WriteLine($"Duplicates   → _duples   : {result.DuplicateFiles.Count}");
        Console.WriteLine($"Conflicts (skipped)       : {result.Conflicts.Count}");

        if (dryRun)
        {
            if (result.UniqueFiles.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("[DRY RUN] Would move to outfolder:");
                foreach (var (src, dst) in result.UniqueFiles)
                    Console.WriteLine($"  {src}  →  {dst}");
            }

            if (result.DuplicateFiles.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("[DRY RUN] Would move to _duples:");
                foreach (var (src, dst) in result.DuplicateFiles)
                    Console.WriteLine($"  {src}  →  {dst}");
            }
        }

        if (result.Conflicts.Count > 0)
        {
            Console.WriteLine();
            Console.Error.WriteLine("Conflicts (same relative path, different content — skipped):");
            foreach (var (src, dst) in result.Conflicts)
                Console.Error.WriteLine($"  {src}  conflicts with  {dst}");
        }
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Environment.Exit(1);
    }
}, inFolderArg, outFolderArg, hashOption, dryRunOption, rebuildDbOption);

rootCommand.AddCommand(dedupCommand);
rootCommand.AddCommand(mergeCommand);

return await rootCommand.InvokeAsync(args);

// ── Helpers ──────────────────────────────────────────────────────────────────
static IReadOnlyList<IFilesComparer> BuildComparers(string hash) =>
    new List<IFilesComparer>
    {
        new HashComparer(hash),
        new ByteByByteComparer()
    };
