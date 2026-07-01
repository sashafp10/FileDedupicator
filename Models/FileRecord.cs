namespace FilesDeduplicator.Models;

public sealed class FileRecord
{
    public string RelativePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string HashType { get; set; } = string.Empty;
    public string HashValue { get; set; } = string.Empty;
    public DateTime DateCreated { get; set; }
    public DateTime DateChanged { get; set; }

    /// <summary>
    /// Relative paths of files that were identified as duplicates of this file
    /// and moved to _duples. Populated after a deduplication run.
    /// </summary>
    public List<string> Aliases { get; set; } = new();
}
