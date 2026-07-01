namespace FilesDeduplicator.Utils;

/// <summary>
/// Writes a plain-text operation log to the filesystem.
/// File name: dedup_YYYY-MM-DD_HH-mm-ss.log, placed in the parent of the scanned directory.
/// </summary>
public sealed class OperationLogger : IDisposable
{
    private readonly StreamWriter _writer;
    public string FilePath { get; }

    private OperationLogger(string path)
    {
        FilePath = path;
        _writer  = new StreamWriter(path, append: false, System.Text.Encoding.UTF8)
        {
            AutoFlush = true
        };
    }

    public static OperationLogger Create(string nearFolder, string prefix = "dedup")
    {
        nearFolder = Path.GetFullPath(nearFolder);
        var parent = Path.GetDirectoryName(nearFolder) ?? nearFolder;
        var stamp  = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var path   = Path.Combine(parent, $"{prefix}_{stamp}.log");
        return new OperationLogger(path);
    }

    public void WriteLine(string line = "") => _writer.WriteLine(line);
    public void Write(string text)          => _writer.Write(text);

    public void Dispose() => _writer.Dispose();
}
