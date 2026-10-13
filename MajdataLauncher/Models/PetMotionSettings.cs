using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MajdataLauncher.Models;

internal sealed class PetMotionSettings
{
    public double BreathingIntervalSeconds { get; set; } = 1.4;
    public double BreathingDownSeconds { get; set; } = .4;
    public double BreathingUpSeconds { get; set; } = .4;
    public bool ElasticityEnabled { get; set; } = true;
    public bool DragGravityEnabled { get; set; } = true;
    public double ClickExpressionCooldownSeconds { get; set; } = 6;

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal static PetMotionSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            var document = JsonNode.Parse(File.ReadAllText(path), new JsonNodeOptions { PropertyNameCaseInsensitive = true }) as JsonObject;
            if (document == null) return new();
            var legacy = document.ContainsKey("squashSpeed") || document.ContainsKey("springSpeed") || document.ContainsKey("breathingPeriodSeconds");
            if (legacy && !document.ContainsKey("breathingIntervalSeconds") &&
                !document.ContainsKey("breathingDownSeconds") && !document.ContainsKey("breathingUpSeconds"))
            {
                var squash = 1 / ReadLegacy(document, "squashSpeed", 20, 1, 300);
                var spring = 1 / ReadLegacy(document, "springSpeed", 35, 1, 300);
                var period = ReadLegacy(document, "breathingPeriodSeconds", 4.2, .5, 60);
                document["breathingIntervalSeconds"] = 0d;
                document["breathingDownSeconds"] = period * squash / (squash + 2 * spring);
                // The original rise and settle together form the upward phase.
                document["breathingUpSeconds"] = period * (2 * spring) / (squash + 2 * spring);
            }
            var settings = document.Deserialize<PetMotionSettings>(Json) ?? new();
            settings.BreathingIntervalSeconds = Positive(settings.BreathingIntervalSeconds, 1.4, 0, 60);
            settings.BreathingDownSeconds = Positive(settings.BreathingDownSeconds, .4, .0001, 60);
            settings.BreathingUpSeconds = Positive(settings.BreathingUpSeconds, .4, .0001, 60);
            settings.ClickExpressionCooldownSeconds = Positive(settings.ClickExpressionCooldownSeconds, 6, 0, 3600);
            if (legacy)
            {
                document.Remove("squashSpeed"); document.Remove("springSpeed"); document.Remove("breathingPeriodSeconds");
                document["breathingIntervalSeconds"] = settings.BreathingIntervalSeconds;
                document["breathingDownSeconds"] = settings.BreathingDownSeconds;
                document["breathingUpSeconds"] = settings.BreathingUpSeconds;
                // Preserve unrelated per-pet settings when upgrading the file.
                var temporary = path + ".migrating-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllText(temporary, document.ToJsonString(Json));
                    File.Move(temporary, path, true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine(error);
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(error); }
                }
            }
            return settings;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(error);
            return new();
        }
    }

    private static double ReadLegacy(JsonObject document, string name, double fallback, double min, double max)
        => document[name] is JsonValue value && value.TryGetValue<double>(out var number)
            ? Positive(number, fallback, min, max) : fallback;

    private static double Positive(double value, double fallback, double min, double max)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
