using System;

namespace MajdataCore
{
    // MajdataPlay SlideBase.Judge: the perfect window expands with the dwell
    // time in the final area. Values match Alpha's JudgeType enum.
    public static class SlideJudgeTiming
    {
        public static int Grade(float now, float expected, float end)
        {
            var extension = Math.Min(Math.Max(0f, end - expected) / 4f, 0.733333f);
            var perfect = Math.Min(0.4666667f + extension, 1.2f) / 2f;
            var great = Math.Max(0.4f - extension, 0f) / 2f;
            var difference = Math.Abs(now - expected);
            if (difference < perfect) return 7;
            if (great > 0f && difference < perfect + great && difference < 0.6f)
                return now < expected ? 10 : 4;
            return now < expected ? 13 : 1;
        }
    }
}
