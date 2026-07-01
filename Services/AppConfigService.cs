using System.Text.Json;
using FilesDeduplicator.Models;

namespace FilesDeduplicator.Services;

public sealed class AppConfigService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly string _configPath;
    private AppConfig? _cache;

    public AppConfigService(string? configPath = null)
    {
        _configPath = configPath
            ?? Path.Combine(AppContext.BaseDirectory, "appconfig.json");

        // Fallback: look next to the exe and in cwd
        if (!File.Exists(_configPath))
        {
            var cwd = Path.Combine(Directory.GetCurrentDirectory(), "appconfig.json");
            if (File.Exists(cwd))
                _configPath = cwd;
        }
    }

    public AppConfig Load()
    {
        if (_cache is not null) return _cache;

        if (!File.Exists(_configPath))
        {
            _cache = new AppConfig();
            return _cache;
        }

        try
        {
            var text = File.ReadAllText(_configPath);
            _cache = JsonSerializer.Deserialize<AppConfig>(text, JsonOpts) ?? new AppConfig();
        }
        catch
        {
            _cache = new AppConfig();
        }

        // Normalise extensions: ensure leading dot, lowercase
        foreach (var preset in _cache.Presets)
            preset.Extensions = preset.Extensions
                .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : $".{e.ToLowerInvariant()}")
                .Distinct()
                .ToList();

        return _cache;
    }
}
