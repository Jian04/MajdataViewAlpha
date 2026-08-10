using System.Text.Json.Serialization;

namespace MajdataLauncher.Models;

internal sealed class LauncherSettings
{
    [JsonPropertyName("pet")]
    public string Pet { get; set; } = "dilaxiong";

    [JsonPropertyName("viewPath")]
    public string ViewPath { get; set; } = string.Empty;

    [JsonPropertyName("editPath")]
    public string EditPath { get; set; } = string.Empty;

    [JsonPropertyName("followEditWindow")]
    public bool FollowEditWindow { get; set; } = true;

    [JsonPropertyName("viewReadyPort")]
    public int ViewReadyPort { get; set; } = 8013;

    [JsonPropertyName("viewReadyTimeoutSeconds")]
    public int ViewReadyTimeoutSeconds { get; set; } = 15;

    [JsonPropertyName("petControlPort")]
    public int PetControlPort { get; set; } = 8015;
}
