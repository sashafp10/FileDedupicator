using FilesDeduplicator.Interfaces;

namespace FilesDeduplicator.Comparers;

/// <summary>
/// Compares files byte-by-byte.
/// Returns 2 if files are identical, 0 otherwise.
/// This comparer is the definitive confirmation step in the pipeline.
/// </summary>
public sealed class ByteByByteComparer : IFilesComparer
{
    private const int BufferSize = 81920; // 80 KB

    public int EqualsIndex(string path1, string path2)
    {
        var info1 = new FileInfo(path1);
        var info2 = new FileInfo(path2);

        if (info1.Length != info2.Length)
            return 0;

        using var fs1 = new FileStream(path1, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
        using var fs2 = new FileStream(path2, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);

        var buf1 = new byte[BufferSize];
        var buf2 = new byte[BufferSize];

        int read;
        while ((read = fs1.Read(buf1, 0, BufferSize)) > 0)
        {
            int read2 = 0;
            while (read2 < read)
            {
                int r = fs2.Read(buf2, read2, read - read2);
                if (r == 0) return 0;
                read2 += r;
            }

            if (!buf1.AsSpan(0, read).SequenceEqual(buf2.AsSpan(0, read)))
                return 0;
        }

        return 2;
    }
}
