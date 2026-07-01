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
var filterOption = new Option<string?>(
    name: "--filter",
    description: "Comma-separated preset keys or raw extensions to restrict comparison (e.g. 'images,videos' or '.jpg,.png'). Unfiltered files are copied as-is (merge) or skipped (dedup).");
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
dedupCommand.AddOption(filterOption);

dedupCommand.SetHandler((string dir, string hash, bool dryRun, bool report, string? filterArg) =>
{
    var fullDir = Path.GetFullPath(dir);
    if (!Directory.Exists(fullDir))
    {
        Console.Error.WriteLine($"Directory not found: {fullDir}");
        Environment.Exit(1);
    }

    var filter = BuildFilter(filterArg);

    Console.WriteLine($"Deduplicating: {fullDir}");
    Console.WriteLine($"Hash         : {hash.ToUpperInvariant()}");
    Console.WriteLine($"Dry run      : {dryRun}");
    if (!filter.IsPassAll)
        Console.WriteLine($"Filter       : {filter}");
    Console.WriteLine();

    var comparers = BuildComparers(hash);
    var service   = new DeduplicatorService(comparers, filter);
    var result = service.Deduplicate(fullDir, dryRun);

    Console.WriteLine();
    Console.WriteLine($"Scanned : {result.TotalScanned} files");
    if (result.SkippedByFilter > 0)
        Console.WriteLine($"Skipped : {result.SkippedByFilter} (not in filter)");
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
        var reportPath = HtmlReportService.Generate(result, fullDir, isDryRun: dryRun);
        Console.WriteLine();
        Console.WriteLine($"Report: {reportPath}");
    }
}, dedupDirArg, hashOption, dryRunOption, reportOption, filterOption);

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
mergeCommand.AddOption(filterOption);

mergeCommand.SetHandler((string inFolder, string outFolder, string hash, bool dryRun, bool rebuildDb, string? filterArg) =>
{
    var fullIn  = Path.GetFullPath(inFolder);
    var fullOut = Path.GetFullPath(outFolder);

    if (!Directory.Exists(fullIn))
    {
        Console.Error.WriteLine($"infolder not found: {fullIn}");
        Environment.Exit(1);
    }

    var filter = BuildFilter(filterArg);

    Console.WriteLine($"Input folder : {fullIn}");
    Console.WriteLine($"Output folder: {fullOut}");
    Console.WriteLine($"Hash         : {hash.ToUpperInvariant()}");
    Console.WriteLine($"Dry run      : {dryRun}");
    Console.WriteLine($"Rebuild DB   : {rebuildDb}");
    if (!filter.IsPassAll)
        Console.WriteLine($"Filter       : {filter}");
    Console.WriteLine();

    var comparers = BuildComparers(hash);
    var dbService = new FilesDbService(hash);
    var service   = new MergeService(comparers, dbService, filter);

    try
    {
        var result = service.Merge(fullIn, fullOut, dryRun, rebuildDb, Console.WriteLine);

        Console.WriteLine();
        Console.WriteLine($"Unique files → outfolder : {result.UniqueFiles.Count}");
        Console.WriteLine($"Duplicates   → _duples   : {result.DuplicateFiles.Count}");
        Console.WriteLine($"Conflicts (skipped)       : {result.Conflicts.Count}");
        if (result.PassthroughFiles.Count > 0)
            Console.WriteLine($"Passthrough (filter)      : {result.PassthroughFiles.Count}");

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
}, inFolderArg, outFolderArg, hashOption, dryRunOption, rebuildDbOption, filterOption);

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
static FileTypeFilter BuildFilter(string? filterArg)
{
    if (string.IsNullOrWhiteSpace(filterArg))
        return FileTypeFilter.PassAll;

    var configService = new AppConfigService();
    var config        = configService.Load();

    // Tokens are either preset keys (e.g. "images") or raw extensions (e.g. ".jpg")
    var presetKeys      = new List<string>();
    var customFragments = new List<string>();

    foreach (var token in filterArg.Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
        var t = token.Trim();
        if (config.Presets.Any(p => string.Equals(p.Key, t, StringComparison.OrdinalIgnoreCase)))
            presetKeys.Add(t);
        else
            customFragments.Add(t);
    }

    return FileTypeFilter.Build(config, presetKeys, customFragments.Count > 0 ? string.Join(",", customFragments) : null);
}