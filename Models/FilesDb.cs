namespace FilesDeduplicator.Models;

public sealed class FilesDb
{
    public string RootPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string HashAlgorithm { get; set; } = string.Empty;
    public List<FileRecord> Files { get; set; } = new();
}
