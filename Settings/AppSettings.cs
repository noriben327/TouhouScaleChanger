using TouhouScalePad.Models;

namespace TouhouScalePad.Settings;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public bool MonitoringEnabled { get; set; } = true;
    public int PollingIntervalMilliseconds { get; set; } = 2;
    public List<SizePreset> SizePresets { get; set; } = [];
    public List<GameProfile> GameProfiles { get; set; } = [];

    public static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            SizePresets =
            [
                BuiltIn("4:3 / 640×480", 640, 480, "4:3"),
                BuiltIn("4:3 / 800×600", 800, 600, "4:3"),
                BuiltIn("4:3 / 1024×768", 1024, 768, "4:3"),
                BuiltIn("4:3 / 1280×960", 1280, 960, "4:3"),
                BuiltIn("4:3 / 1400×1050", 1400, 1050, "4:3"),
                BuiltIn("4:3 / 1600×1200", 1600, 1200, "4:3"),
                BuiltIn("4:3 / 1920×1440", 1920, 1440, "4:3"),
                BuiltIn("16:9 / 1280×720", 1280, 720, "16:9"),
                BuiltIn("16:9 / 1600×900", 1600, 900, "16:9"),
                BuiltIn("16:9 / 1920×1080", 1920, 1080, "16:9"),
                BuiltIn("16:9 / 2560×1440", 2560, 1440, "16:9"),
                BuiltIn("16:9 / 3840×2160", 3840, 2160, "16:9")
            ]
        };
    }

    private static SizePreset BuiltIn(string name, int width, int height, string aspectGroup) => new()
    {
        Name = name,
        Width = width,
        Height = height,
        AspectGroup = aspectGroup,
        IsBuiltIn = true
    };
}
