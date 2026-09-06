using System;

namespace Newtonsoft.Json
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    internal sealed class JsonIgnoreAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    internal sealed class JsonPropertyAttribute : Attribute
    {
        public JsonPropertyAttribute(string name)
        {
        }
    }
}

namespace System.Windows
{
    internal static class MessageBox
    {
        public static void Show(string message, string title)
        {
        }
    }
}

namespace UnityEngine
{
    /// <summary>
    /// The single engine call SvController makes. Stubbing it lets the bounce and
    /// scroll maths be tested off Windows, where the rest of the player cannot
    /// build; the formula is Unity's own documented behaviour.
    /// </summary>
    internal static class Mathf
    {
        public const float PI = MathF.PI;
        public const float Deg2Rad = MathF.PI / 180f;
        public static float Sin(float x) => MathF.Sin(x);
        public static float Cos(float x) => MathF.Cos(x);
        public static float Acos(float x) => MathF.Acos(x);
        public static float Atan2(float y, float x) => MathF.Atan2(y, x);
        public static float Sqrt(float x) => MathF.Sqrt(x);
        public static float Abs(float x) => MathF.Abs(x);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Clamp(float x, float a, float b) => Math.Clamp(x, a, b);
        public static int CeilToInt(float x) => (int)MathF.Ceiling(x);
        public static float Repeat(float x, float length) => Math.Clamp(x - MathF.Floor(x / length) * length, 0f, length);
        public static float Clamp01(float value) =>
            value < 0f ? 0f : value > 1f ? 1f : value;
    }

    internal readonly struct Vector2
    {
        public readonly float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0, 0);
        public float magnitude => MathF.Sqrt(x * x + y * y);
        public Vector2 normalized => magnitude == 0f ? zero : this / magnitude;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float b) => new(a.x * b, a.y * b);
        public static Vector2 operator /(Vector2 a, float b) => new(a.x / b, a.y / b);
        public static implicit operator Vector2(Vector3 a) => new(a.x, a.y);
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * Mathf.Clamp01(t);
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
    }

    internal readonly struct Vector3
    {
        public readonly float x, y, z;
        public Vector3(float x, float y, float z = 0) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static implicit operator Vector3(Vector2 a) => new(a.x, a.y);
    }
}

namespace System.Windows.Media.Animation
{
}

namespace System.Windows.Navigation
{
}

namespace MajdataEdit
{
    internal static class MainWindow
    {
        public static string GetLocalizedString(string key) => key;
    }

    internal static class ThemeManager
    {
        public const string DefaultTheme = "default";
    }

    public class ErrorInfo
    {
        public ErrorInfo(int positionX, int positionY)
        {
        }
    }
}
