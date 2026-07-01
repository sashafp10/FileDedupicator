namespace FilesDeduplicator.Interfaces;

/// <summary>
/// Compares two files for equality.
/// Returns 0 if the files are NOT equal (pipeline stops).
/// Returns a positive integer if the files ARE equal at this level of confidence
/// (higher value = stronger match; pipeline continues to next comparer).
/// </summary>
public interface IFilesComparer
{
    int EqualsIndex(string path1, string path2);
}
