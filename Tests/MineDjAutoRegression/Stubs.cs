namespace UnityEngine
{
    public class MonoBehaviour { }
    public enum KeyCode { None }
    public static class Debug
    {
        public static void LogWarning(object message) => throw new System.Exception(message.ToString());
    }
}

// This regression isolates ordinary sensor contacts from active block areas.
public static class NoiseZoneInputGate
{
    public static bool IsBlocked(Assets.Scripts.Types.SensorType type) => false;
}
