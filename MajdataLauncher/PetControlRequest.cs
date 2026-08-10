namespace MajdataLauncher;

internal sealed class PetControlRequest
{
    public string Action { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public double? Angle { get; init; }
}
