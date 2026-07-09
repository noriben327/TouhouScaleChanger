namespace TouhouScalePad.Models;

using System.Text.Json.Serialization;

public sealed class GameProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GameName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public Guid SizePresetId { get; set; }
    public bool DpadMappingEnabled { get; set; } = true;
    public bool IsEnabled { get; set; } = true;

    [JsonIgnore]
    public string DisplayDetail { get; set; } = string.Empty;

    public override string ToString() => GameName;
}
