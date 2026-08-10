using System.Text.Json.Serialization;

namespace MajdataLauncher.Models;

internal sealed class PetManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "dilaxiong";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "Dilaxiong";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "Majdata companion.";

    [JsonPropertyName("spriteVersionNumber")]
    public int SpriteVersionNumber { get; set; }

    [JsonPropertyName("spritesheetPath")]
    public string SpritesheetPath { get; set; } = string.Empty;
}
