using UnityEngine;
using Assets.Scripts.Types;
#nullable enable
namespace Assets.Scripts
{
    public class TouchBase : NoteDrop
    {
        public char areaPosition;
        public bool isFirework;
        public bool isBreak;
        public bool isMine;
        public Material colorOverrideMaterial;
        public float noteScale = 1f;
        public float noteScaleX = 1f;
        public float noteScaleY = 1f;
        private Vector2? liveScaleDefault;

        public GameObject tapEffect;
        public GameObject judgeEffect;

        public override void ApplyLiveScale(Vector2? scale)
        {
            liveScaleDefault ??= new Vector2(noteScaleX, noteScaleY);
            var previous = new Vector2(noteScaleX, noteScaleY);
            var value = scale ?? liveScaleDefault.Value;
            noteScaleX = value.x;
            noteScaleY = value.y;
            if (previous.x != 0f && previous.y != 0f)
                transform.localScale = Vector3.Scale(
                    transform.localScale,
                    new Vector3(value.x / previous.x, value.y / previous.y, 1f));
        }


        protected Sprite[] judgeText;
        protected Sprite judgeTextBreak;
        public TouchGroup GroupInfo;

        protected Quaternion GetRoation()
        {
            if (sensor.Type == SensorType.C)
                return Quaternion.Euler(Vector3.zero);
            var d = -GetFixedFeedbackPosition();
            var deg = 180 + Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;

            return Quaternion.Euler(new Vector3(0, 0, -deg));
        }
        public SensorType GetSensor() => GetSensor(areaPosition, startPosition);
        public static SensorType GetSensor(char areaPos, int startPos)
        {
            switch (areaPos)
            {
                case 'A':
                    return (SensorType)(startPos - 1);
                case 'B':
                    return (SensorType)(startPos + 7);
                case 'C':
                    return SensorType.C;
                case 'D':
                    return (SensorType)(startPos + 16);
                case 'E':
                    return (SensorType)(startPos + 24);
                default:
                    return SensorType.A1;
            }
        }
    }
}
