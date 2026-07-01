namespace FilesDeduplicator.Models;

public sealed class FilterPreset
{
    public string Name { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public List<string> Extensions { get; set; } = new();
}

public sealed class AppConfig
{
    public List<FilterPreset> Presets { get; set; } = new();
}
