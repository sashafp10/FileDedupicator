using FilesDeduplicator.Models;

namespace FilesDeduplicator.Services;

/// <summary>
/// Determines whether a file should be included in deduplication / merge comparison
/// based on the active extension whitelist.
///
/// If the filter is empty (no presets / extensions selected) all files are included.
/// </summary>
public sealed class FileTypeFilter
{
    public static readonly FileTypeFilter PassAll = new(new HashSet<string>());

    private readonly HashSet<string> _extensions;

    private FileTypeFilter(HashSet<string> extensions)
    {
        _extensions = extensions;
    }

    /// <summary>True when the filter allows all files (no restriction configured).</summary>
    public bool IsPassAll => _extensions.Count == 0;

    /// <summary>Returns true if this file's extension is within the active filter (or filter is empty).</summary>
    public bool Includes(string filePath)
    {
        if (IsPassAll) return true;
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return _extensions.Contains(ext);
    }

    /// <summary>Build from preset keys + optional raw custom extension string ("cs,exe,log").</summary>
    public static FileTypeFilter Build(
        AppConfig config,
        IEnumerable<string> selectedPresetKeys,
        string? customExtensions = null)
    {
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in selectedPresetKeys)
        {
            var preset = config.Presets.FirstOrDefault(p =>
                string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            if (preset is not null)
                foreach (var e in preset.Extensions)
                    exts.Add(e);
        }

        if (!string.IsNullOrWhiteSpace(customExtensions))
        {
            foreach (var raw in customExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var e = raw.Trim().ToLowerInvariant();
                if (!e.StartsWith('.')) e = $".{e}";
                exts.Add(e);
            }
        }

        return new FileTypeFilter(exts);
    }

    public IReadOnlyCollection<string> ActiveExtensions => _extensions;

    public override string ToString() =>
        IsPassAll ? "(all files)" : string.Join(", ", _extensions.Order());
}
