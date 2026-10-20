using System.Text;
using System.Text.Json;

namespace SamaLite;

internal sealed class Settings
{
    public int Rotation { get; set; } = 270;
    public int RefreshSeconds { get; set; } = 1;
    public int? Brightness { get; set; }
    public bool Paused { get; set; }
    public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SamaLite");
    private static string FileName => Path.Combine(DataDirectory, "settings.json");
    public static Settings Load()
    {
        Directory.CreateDirectory(DataDirectory);
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FileName)) ?? new Settings();
            if (settings.Rotation is not (0 or 90 or 180 or 270)) settings.Rotation = 270;
            if (settings.RefreshSeconds is not (1 or 2 or 5)) settings.RefreshSeconds = 1;
            if (settings.Brightness is < 0 or > 255) settings.Brightness = null;
            return settings;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { return new Settings(); }
    }
    public void Save() => File.WriteAllText(FileName, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
}
