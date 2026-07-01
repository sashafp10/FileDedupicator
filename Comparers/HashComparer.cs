using System.Security.Cryptography;
using FilesDeduplicator.Interfaces;

namespace FilesDeduplicator.Comparers;

/// <summary>
/// Compares files by computing their cryptographic hash.
/// Returns 1 if hashes match, 0 otherwise.
/// </summary>
public sealed class HashComparer : IFilesComparer
{
    private readonly string _algorithm;

    public HashComparer(string algorithm = "MD5")
    {
        _algorithm = algorithm.ToUpperInvariant();
    }

    public int EqualsIndex(string path1, string path2)
    {
        var h1 = ComputeHash(path1);
        var h2 = ComputeHash(path2);
        return string.Equals(h1, h2, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    public string ComputeHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920);
        using HashAlgorithm ha = _algorithm switch
        {
            "SHA256" => SHA256.Create(),
            "SHA1"   => SHA1.Create(),
            _        => MD5.Create()
        };
        return Convert.ToHexString(ha.ComputeHash(stream));
    }
}
