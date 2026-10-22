using System;
using System.IO;
using System.Text.Json;

namespace SolutionReplicator.Services;

/// <summary>
/// Last-used paths remembered between runs, persisted as JSON under
/// <c>%LOCALAPPDATA%\SolutionReplicator\settings.json</c>. Pre-fills the browse fields so a user
/// standing up many systems does not re-browse to the same catalog/repo every launch.
/// </summary>
public sealed class AppSettings
{
    public string LastTemplateXml { get; set; } = string.Empty;
    public string LastRepoFolder { get; set; } = string.Empty;
    public string LastCatalogPath { get; set; } = string.Empty;
    public string LastOutputFolder { get; set; } = string.Empty;
    public string LastProfile { get; set; } = string.Empty;
}

/// <summary>Loads/saves <see cref="AppSettings"/>. Failures are swallowed — settings are a
/// convenience, never load-bearing.</summary>
public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string SettingsPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SolutionReplicator");
            return Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
                return new AppSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var path = SettingsPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // Best-effort; never break the flow because settings couldn't be written.
        }
    }
}
