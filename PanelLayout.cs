namespace SamaLite;

internal sealed record PanelLayout
{
    public string Preset { get; init; } = "dense";
    public string[] Slots { get; init; } = ["fps", "frametime", "low", "fan", "pump", "nvme", "download", "upload", "disk-read", "disk-write", "dimm", "igpu"];
    public bool Rings { get; init; } = true;
    public static readonly string[] Choices = ["fps", "frametime", "low", "fan", "pump", "nvme", "download", "upload", "disk-read", "disk-write", "disk-busy", "dimm", "igpu", "cpu-power", "gpu-power"];
    public PanelLayout Normalized() => this with { Preset = Preset is "dense" or "game" or "cooling" ? Preset : "dense",
        Slots = Enumerable.Range(0, 12).Select(i => Slots is not null && i < Slots.Length && Choices.Contains(Slots[i], StringComparer.Ordinal)
            ? Slots[i] : new PanelLayout().Slots[i]).ToArray() };
    public static PanelLayout For(string preset) => preset switch
    {
        "game" => new() { Preset = preset, Slots = ["fps", "frametime", "low", "gpu-power", "cpu-power", "nvme", "download", "upload", "disk-read", "disk-write", "dimm", "igpu"] },
        "cooling" => new() { Preset = preset, Slots = ["fan", "pump", "nvme", "dimm", "cpu-power", "gpu-power", "download", "upload", "disk-read", "disk-write", "fps", "frametime"] },
        _ => new()
    };
}
