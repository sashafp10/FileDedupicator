# Files Deduplicator

A cross-platform .NET CLI tool to find and manage duplicate files across directories with visual HTML reports and flexible comparison strategies.

## Features

- **Two deduplication modes:**
  - **Tool 1:** Find duplicates within a single directory → move to `_duples/` subfolder
  - **Tool 2:** Merge two folders → move unique files to output folder, duplicates to `_duples/` in input folder

- **Smart duplicate detection:**
  - File size → hash (MD5/SHA1/SHA256) → byte-by-byte comparison pipeline
  - Pluggable comparer interface (`IFilesComparer`)
  - Preserves original folder structure

- **Intelligent keeper selection:**
  - Prefers files with "original-looking" names (avoids `(1)`, `_copy`, `(copy)`, etc.)
  - Falls back to creation date → alphabetical order
  - Marks ambiguous groups (multiple files with equally original names)

- **Visual HTML report:**
  - Dark-themed card grid showing all duplicates side-by-side
  - Image/video previews (via `file://` URIs)
  - File metadata: size, created date, modified date
  - No server required — open locally in browser

- **Operation logs:**
  - Timestamped logs (`dedup_YYYY-MM-DD_HH-mm-ss.log`) next to each scanned folder
  - Full duplicate groups with ambiguous markers

- **Interactive menu or CLI:**
  - Menu mode: run without args for guided workflow
  - CLI mode: `dedup` and `merge` commands with options

- **Progress indicators:**
  - Throttled "X / Y [%]" counter (updates ~1/sec)
  - File moves printed as permanent lines

## Installation

### Prerequisites
- .NET 8.0 SDK

### Build from source
```bash
git clone https://github.com/sashafp10/FileDedupicator.git
cd FilesDeduplicator
dotnet build
```

### Run
```bash
# Interactive menu (no arguments)
dotnet run

# Or use as CLI
dotnet run -- dedup /path/to/folder
dotnet run -- merge /path/to/in /path/to/out
```

## Usage

### Interactive Menu (simplest)

Run with no arguments:
```bash
dotnet run
```

Menu options:
- **1 — Deduplicate:** Scan a folder, show groups, optionally generate HTML report and operation log
- **2 — Merge:** Move unique files from one folder to another, duplicates to `_duples/`
- **3 — Review ambiguous groups:** Rename kept files that had equally original-looking names

### CLI Mode

#### Dedup command
```bash
filesdedup dedup <directory> [options]

Options:
  --hash <MD5|SHA1|SHA256>  Hash algorithm (default: MD5)
  --dry-run                 Preview without moving files
  --report                  Generate HTML report after scan
```

Example:
```bash
dotnet run -- dedup ~/Pictures --hash SHA256 --dry-run --report
```

#### Merge command
```bash
filesdedup merge <infolder> <outfolder> [options]

Options:
  --hash <MD5|SHA1|SHA256>  Hash algorithm (default: MD5)
  --dry-run                 Preview without moving files
  --rebuild-db              Ignore cached file databases
```

Example:
```bash
dotnet run -- merge ~/Downloads ~/Archive --dry-run
```

## How It Works

### Deduplication (single folder)

1. **Scan** all files (excluding `_duples/`)
2. **Group by size** — cheap filter
3. **Union-find** connected components via comparison pipeline:
   - Hash compare → Byte-by-byte compare
4. **Pick keeper** per group:
   - Lowest copy-name score (0 = most original)
   - If tied: oldest creation date
   - If tied: shortest filename
   - If tied: alphabetical
5. **Move** duplicates to `<folder>/_duples/<original relative path>`

### Merge (two folders)

1. **Build/load JSON DBs** (cached next to each folder):
   - File record: relative path, filename, size, hash, creation date, mod date
2. **Pre-filter by size**, then by hash (fast, no file I/O)
3. **Run comparison pipeline** to confirm (byte-by-byte by default)
4. **Move:**
   - Unique files → output folder
   - Duplicates → `<input>/_duples/`

### Comparison Pipeline

Files are compared via an ordered list of `IFilesComparer` implementations:

```csharp
public interface IFilesComparer
{
    // Returns 0 = NOT equal (stop), >0 = equal at this level (continue)
    int EqualsIndex(string path1, string path2);
}
```

Default pipeline:
1. **HashComparer** (returns 1 if hashes match)
2. **ByteByByteComparer** (returns 2 if byte-identical)

If any comparer returns 0, files are not equal. Otherwise, they're considered identical.

## Copy-Name Scoring

Files are scored for how "original" their names look. Higher score = more likely a duplicate/copy:

| Pattern | Score |
|---------|-------|
| No special pattern | 0 |
| `filename_1`, `filename_2` | 4 |
| `filename (1)`, `filename (2)` | 5 |
| `filename_copy` | 7 |
| `filename (copy)` | 8 |
| `filename - Copy` | 8 |
| `Copy of filename` | 10 |

## Output Structure

After running deduplication:
```
/path/to/folder/
├── file1.jpg
├── file2.jpg
├── _duples/
│   ├── file1_1.jpg          (duplicate, preserved structure)
│   └── subfolder/
│       └── file_copy.jpg
├── dedup_2026-07-01_14-30-00.log          (operation log)
└── subfolder/
    └── file3.jpg
```

Report file:
```
/path/to/folder_duplicates_report.html     (side-by-side visual)
```

## Examples

### Find duplicates in your Pictures folder

```bash
dotnet run -- dedup ~/Pictures --hash SHA256 --report
```

Generates:
- `~/Pictures_duplicates_report.html` — open in browser
- `~/dedup_<timestamp>.log` — text summary

### Merge two photo libraries

```bash
dotnet run -- merge ~/Pictures ~/Archive --dry-run
```

Preview what would happen (dry-run), then run without `--dry-run` to actually move files.

### Rename ambiguous originals

1. Run dedup: `dotnet run` (menu option 1)
2. After scan, use menu option 3 to review ambiguous groups
3. Rename kept files one by one

## Architecture

- **Models:** `FileRecord`, `FilesDb`, `DeduplicationResult`, `MergeResult`, `DuplicateGroup`
- **Services:**
  - `DeduplicatorService` — union-find grouping, keeper selection
  - `MergeService` — cross-folder comparison
  - `FilesDbService` — build/load/save JSON file databases
  - `HtmlReportService` — generate visual report
  - `ProgressReporter` — throttled console progress
- **Interfaces:** `IFilesComparer` — pluggable comparison strategies
- **Comparers:** `HashComparer`, `ByteByByteComparer`
- **Utils:** `CopyNameScorer`, `OperationLogger`

## Performance Notes

- **First scan is slow** due to hashing all files
- **JSON DB caching** makes re-runs instant (use `--rebuild-db` to force refresh)
- **Hash algorithm choice:**
  - MD5: fastest, weak collision resistance (good for dedup)
  - SHA1: moderate speed, deprecated for security
  - SHA256: slower, cryptographic strength
- **For millions of files:** pre-filter by size, then by hash before byte-by-byte

## License

MIT (or your chosen license)

## Contributing

Issues and pull requests welcome!
