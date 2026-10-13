using System;

namespace MajdataCore
{
    public static class EffectEnvelope
    {
        public static float Evaluate(float elapsed, float duration, float attack,
            float hold, float release, bool triangular)
        {
            if (elapsed < 0f || elapsed > duration) return 0f;
            float value;
            if (attack >= 0f)
            {
                if (elapsed < attack) value = attack <= 0f ? 1f : elapsed / attack;
                else if (elapsed <= attack + hold) value = 1f;
                else value = release > 0f ? 1f - (elapsed - attack - hold) / release : 0f;
            }
            else
            {
                var progress = duration <= 0f ? 1f : elapsed / duration;
                value = triangular ? 1f - Math.Abs(progress * 2f - 1f)
                    : Math.Min(progress * 10f, (1f - progress) * 10f);
            }
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
