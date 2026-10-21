using System.Text;
using System.Text.Json;

namespace SamaLite;

internal sealed class Settings
{
    public int Rotation { get; set; } = 270;
    public int RefreshSeconds { get; set; } = 1;
    public int? Brightness { get; set; }
    public bool Paused { get; set; }
    public bool AutoReconnect { get; set; } = true;
    public bool NotifyOnDisconnect { get; set; } = true;
    public bool AttentionEnabled { get; set; } = true;
    public int CpuAttention { get; set; } = 90;
    public int GpuAttention { get; set; } = 80;
    public int MemoryAttention { get; set; } = 90;
    public PanelLayout Layout { get; set; } = new();
    public string CpuFanSensorId { get; set; } = "";
    public string PumpSensorId { get; set; } = "";
    public string NvmeDeviceId { get; set; } = "";
    public string NetworkAdapterId { get; set; } = "";
    public bool FramesEnabled { get; set; } = true;
    public string GameProcess { get; set; } = "";
    public string GameDisplay { get; set; } = "auto";
    public SettingsSnapshot Capture()
    {
        lock (this) return new(Rotation, RefreshSeconds, Brightness, Paused, AutoReconnect, AttentionEnabled, CpuAttention, GpuAttention, MemoryAttention)
        { Layout = (Layout ?? new()).Normalized(), CpuFanSensorId = CpuFanSensorId ?? "", PumpSensorId = PumpSensorId ?? "",
            NvmeDeviceId = NvmeDeviceId ?? "", NetworkAdapterId = NetworkAdapterId ?? "", FramesEnabled = FramesEnabled, GameProcess = GameProcess ?? "", GameDisplay = GameDisplay is "game" or "normal" ? GameDisplay : "auto" };
    }
    public static string DataDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "data");
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
            settings.CpuAttention = Math.Clamp(settings.CpuAttention, 40, 120);
            settings.GpuAttention = Math.Clamp(settings.GpuAttention, 40, 120);
            settings.MemoryAttention = Math.Clamp(settings.MemoryAttention, 50, 100);
            settings.Layout = (settings.Layout ?? new()).Normalized();
            return settings;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { return new Settings(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        var pending = FileName + ".new";
        File.WriteAllText(pending, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(pending, FileName, true);
    }
}

internal sealed record SettingsSnapshot(int Rotation, int RefreshSeconds, int? Brightness, bool Paused, bool AutoReconnect,
    bool AttentionEnabled, int CpuAttention, int GpuAttention, int MemoryAttention)
{
    public PanelLayout Layout { get; init; } = new();
    public string CpuFanSensorId { get; init; } = "";
    public string PumpSensorId { get; init; } = "";
    public string NvmeDeviceId { get; init; } = "";
    public string NetworkAdapterId { get; init; } = "";
    public bool FramesEnabled { get; init; } = true;
    public string GameProcess { get; init; } = "";
    public string GameDisplay { get; init; } = "auto";
}
