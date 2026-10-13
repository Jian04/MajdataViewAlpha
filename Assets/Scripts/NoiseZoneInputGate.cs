using System;
using Assets.Scripts.Types;

// Evaluate at the caller's audio time, not in a frame-delayed Update cache.
public static class NoiseZoneInputGate
{
    private static object owner;
    private static Func<int, bool> blocked;
    public static bool IsBlocked(SensorType sensor) => blocked?.Invoke((int)sensor) == true;
    public static void Bind(object source, Func<int, bool> query) { owner = source; blocked = query; }
    public static void Clear(object source)
    {
        if (!ReferenceEquals(owner, source)) return;
        owner = null; blocked = null;
    }
}
