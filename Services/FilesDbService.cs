using System.Security.Cryptography;
using System.Text.Json;
using FilesDeduplicator.Models;

namespace FilesDeduplicator.Services;

/// <summary>
/// Builds, saves, and loads the JSON file database for a folder.
/// The DB file is saved next to the folder (in its parent directory).
/// </summary>
public sealed class FilesDbService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _hashAlgorithm;

    public FilesDbService(string hashAlgorithm = "MD5")
    {
        _hashAlgorithm = hashAlgorithm.ToUpperInvariant();
    }

    /// <summary>
    /// Returns the path where the DB JSON will be stored.
    /// Placed next to (same parent as) the folder.
    /// </summary>
    public string GetDbPath(string folderPath)
    {
        var fullPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(fullPath) ?? fullPath;
        var folderName = Path.GetFileName(fullPath);
        return Path.Combine(parent, $"{folderName}_files_db.json");
    }

    /// <summary>
    /// Scans all files in <paramref name="rootPath"/> and builds a FilesDb.
    /// Files under _duples subfolders are excluded.
    /// </summary>
    public FilesDb BuildDb(string rootPath, Action<string>? onFile = null)
    {
        rootPath = Path.GetFullPath(rootPath);
        var db = new FilesDb
        {
            RootPath = rootPath,
            CreatedAt = DateTime.UtcNow,
            HashAlgorithm = _hashAlgorithm
        };

        foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(rootPath, filePath);

            // Skip _duples folders at any depth
            if (IsInDuplesFolder(relative))
                continue;

            onFile?.Invoke(relative);

            var info = new FileInfo(filePath);
            db.Files.Add(new FileRecord
            {
                RelativePath = relative,
                FileName = info.Name,
                SizeBytes = info.Length,
                HashType = _hashAlgorithm,
                HashValue = ComputeHash(filePath),
                DateCreated = info.CreationTimeUtc,
                DateChanged = info.LastWriteTimeUtc
            });
        }

        return db;
    }

    public void SaveDb(FilesDb db, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(db, JsonOptions));
    }

    /// <summary>
    /// Loads DB from disk. Returns null if file not found.
    /// If the stored hash algorithm differs from the current setting, returns null (force rebuild).
    /// </summary>
    public FilesDb? LoadDb(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var db = JsonSerializer.Deserialize<FilesDb>(File.ReadAllText(path));
            if (db is null) return null;

            // Hash algorithm mismatch → must rebuild
            if (!string.Equals(db.HashAlgorithm, _hashAlgorithm, StringComparison.OrdinalIgnoreCase))
                return null;

            return db;
        }
        catch
        {
            return null;
        }
    }

    private string ComputeHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920);
        using HashAlgorithm ha = _hashAlgorithm switch
        {
            "SHA256" => SHA256.Create(),
            "SHA1"   => SHA1.Create(),
            _        => MD5.Create()
        };
        return Convert.ToHexString(ha.ComputeHash(stream));
    }

    private static bool IsInDuplesFolder(string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p.Equals("_duples", StringComparison.OrdinalIgnoreCase));
    }
}
