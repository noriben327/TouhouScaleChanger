namespace TouhouScalePad.Models;

using System.Text.Json.Serialization;

public sealed class SizePreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string AspectGroup { get; set; } = "その他";
    public bool IsBuiltIn { get; set; }

    [JsonIgnore]
    public string TypeLabel => IsBuiltIn ? "標準" : "ユーザー";

    public override string ToString() => $"{Name}  ({Width} × {Height})";
}
