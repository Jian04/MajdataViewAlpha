namespace MajdataCore
{
    // MajdataPlay: key mine heads use the ±50ms perfect window; touch
    // mines resolve without a hit just after their authored timing.
    public static class MineJudgment
    {
        public static bool HeadCleared(float timing, bool touch) => timing > (touch ? 0f : 0.05f);
        public static bool HeadCanHit(float timing, bool touch) => touch
            ? timing >= -0.15f && timing <= 0.316667f
            : timing >= -0.05f && timing <= 0.05f;
    }
}
