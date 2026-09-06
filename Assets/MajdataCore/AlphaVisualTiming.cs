using System;
using System.Collections.Generic;

namespace MajdataCore
{
    public readonly struct ScrollPoint
    {
        public ScrollPoint(double time, double cumulative, float multiplier)
        {
            Time = time;
            Cumulative = cumulative;
            Multiplier = multiplier;
        }

        public double Time { get; }
        public double Cumulative { get; }
        public float Multiplier { get; }
    }

    public readonly struct ScrollChange
    {
        public ScrollChange(
            double time,
            float multiplier,
            int sourcePosition = 0)
        {
            Time = time;
            Multiplier = multiplier;
            SourcePosition = sourcePosition;
        }

        public double Time { get; }
        public float Multiplier { get; }
        public int SourcePosition { get; }
    }

    public enum SpawnVisualMode
    {
        Rewind = 0,
        Once = 1
    }

    /// <summary>
    /// One note's memory of whether it has crossed its spawn ring yet.
    /// </summary>
    /// <remarks>
    /// Answering that question means searching the scroll curve from the start of
    /// the chart, so asking it once per note per frame costs the whole elapsed
    /// chart every frame: the more scroll commands a chart has, the slower it gets,
    /// and it keeps getting slower as it plays. Both halves of the answer are facts
    /// about the past that never change, which is what makes them worth keeping:
    /// a crossing that happened stays happened, and a stretch of time searched
    /// without finding one never has to be searched again. So each note pays for
    /// each stretch of the curve once instead of once per frame.
    /// </remarks>
    public struct SpawnCrossingMemo
    {
        private IReadOnlyList<ScrollPoint> source;
        private double threshold;
        private bool hasThreshold;
        /// <summary>First crossing found, or NaN if none is known.</summary>
        private double crossedAt;
        /// <summary>No crossing exists up to here, or NaN if nothing is known.</summary>
        private double searchedTo;

        public bool HasEverCrossed(
            IReadOnlyList<ScrollPoint> curve,
            double historyStart,
            double time,
            double noteScroll,
            float speed,
            float spawnRadius,
            float destroyRadius)
        {
            if (Math.Abs(speed) <= AlphaVisualTiming.Epsilon)
                return true;

            var current = AlphaVisualTiming.GetSpawnCrossingThreshold(
                noteScroll, speed, spawnRadius, destroyRadius);
            // Everything remembered is about one curve and one ring on it, so a
            // different curve or a moved ring makes all of it someone else's answer.
            if (!hasThreshold ||
                !ReferenceEquals(source, curve) ||
                Math.Abs(current - threshold) > AlphaVisualTiming.Epsilon)
            {
                source = curve;
                threshold = current;
                hasThreshold = true;
                crossedAt = double.NaN;
                searchedTo = double.NaN;
            }

            if (!double.IsNaN(crossedAt) && time >= crossedAt)
                return true;

            // The searched stretch always starts at the beginning, so resuming from
            // its end covers the same ground as starting over.
            var searchStart =
                !double.IsNaN(searchedTo) && searchedTo > historyStart &&
                searchedTo <= time
                    ? searchedTo
                    : historyStart;
            var crossing = AlphaVisualTiming.FindFirstTimeAtCumulativeScroll(
                curve, searchStart, time, threshold, speed > 0f);
            if (double.IsNaN(crossing))
            {
                searchedTo = double.IsNaN(searchedTo)
                    ? time
                    : Math.Max(searchedTo, time);
                return false;
            }
            crossedAt = crossing;
            return true;
        }
    }

    public static class AlphaVisualTiming
    {
        public const float DefaultSpawnRadius = 1.225f;
        public const float DefaultDestroyRadius = 4.8f;
        public const float SpawnScaleDistance = 2.5f;
        public const double Epsilon = 0.000001d;

        /// <summary>
        /// When the playfield takes over from the song card, as a chart time. The
        /// cover plays over negative time and second zero is the first beat, so
        /// everything the player judges against has to already be on screen by
        /// here.
        ///
        /// Notes used to be withheld until zero instead, while the judge line and
        /// the background revealed at this point. A note's approach begins up to
        /// 0.9s before its own judge time, so a note near the start of the chart
        /// spent its entire approach withheld and then appeared already on the
        /// judgement ring: it looked like a note nobody wrote, and it could not be
        /// hit. Two reveal times is what caused that, so there is only one now.
        /// The value is well clear of the longest approach.
        /// </summary>
        public const float GameplayRevealTime = -2f;

        public static float ToViewNoteSpeed(float maiSpeed) =>
            (float)(107.25d /
                    (71.4184491d * Math.Pow(maiSpeed + 0.9975d, -0.985558604d)));

        public static float GetPathDirection(float spawnRadius, float destroyRadius)
        {
            var delta = destroyRadius - spawnRadius;
            return Math.Abs(delta) <= Epsilon ? 1f : Math.Sign(delta);
        }

        public static float GetPathLength(float spawnRadius, float destroyRadius) =>
            Math.Abs(destroyRadius - spawnRadius);

        public static ScrollPoint[] BuildScrollCurve(
            IEnumerable<ScrollChange> changes)
        {
            var ordered = new List<ScrollChange>(
                changes ?? Array.Empty<ScrollChange>());
            ordered.Sort((left, right) =>
            {
                var byTime = left.Time.CompareTo(right.Time);
                return byTime != 0
                    ? byTime
                    : left.SourcePosition.CompareTo(right.SourcePosition);
            });

            if (ordered.Count == 0)
                ordered.Add(new ScrollChange(0d, 1f, int.MinValue));

            var effective = new List<ScrollChange>();
            foreach (var change in ordered)
            {
                if (effective.Count > 0 &&
                    Math.Abs(effective[^1].Time - change.Time) <= Epsilon)
                    effective[^1] = change;
                else
                    effective.Add(change);
            }

            var result = new ScrollPoint[effective.Count];
            var cumulative = 0d;
            for (var index = 0; index < effective.Count; index++)
            {
                if (index > 0)
                {
                    var previous = effective[index - 1];
                    cumulative += previous.Multiplier *
                        (effective[index].Time - previous.Time);
                }
                result[index] = new ScrollPoint(
                    effective[index].Time,
                    cumulative,
                    effective[index].Multiplier);
            }
            return result;
        }

        public static float GetVisualRadius(
            double noteScroll,
            double currentScroll,
            float speed,
            float spawnRadius,
            float destroyRadius) =>
            destroyRadius -
            GetPathDirection(spawnRadius, destroyRadius) *
            speed *
            (float)(noteScroll - currentScroll);

        public static float GetPathPosition(
            float radius,
            float spawnRadius,
            float destroyRadius) =>
            GetPathDirection(spawnRadius, destroyRadius) *
            (radius - spawnRadius);

        public static float GetSpawnScale(
            float radius,
            float spawnRadius,
            float destroyRadius) =>
            (GetPathPosition(radius, spawnRadius, destroyRadius) +
             SpawnScaleDistance) / SpawnScaleDistance;

        public static float GetSpawnPresentationRadius(
            float integratedRadius,
            float spawnRadius,
            bool hasEverCrossedSpawn) =>
            hasEverCrossedSpawn ? integratedRadius : spawnRadius;

        public static bool IsPastSpawnNow(
            double noteScroll,
            double currentScroll,
            float speed,
            float spawnRadius,
            float destroyRadius) =>
            GetPathPosition(
                GetVisualRadius(
                    noteScroll, currentScroll, speed, spawnRadius, destroyRadius),
                spawnRadius,
                destroyRadius) >= -Epsilon;

        /// <summary>
        /// The cumulative scroll a note's spawn ring sits at.
        /// </summary>
        public static double GetSpawnCrossingThreshold(
            double noteScroll,
            float speed,
            float spawnRadius,
            float destroyRadius) =>
            noteScroll - GetPathLength(spawnRadius, destroyRadius) / speed;

        public static bool HasEverCrossedSpawn(
            IReadOnlyList<ScrollPoint> curve,
            double historyStart,
            double time,
            double noteScroll,
            float speed,
            float spawnRadius,
            float destroyRadius)
        {
            if (Math.Abs(speed) <= Epsilon)
                return true;
            var threshold = GetSpawnCrossingThreshold(
                noteScroll, speed, spawnRadius, destroyRadius);
            return !double.IsNaN(FindFirstThresholdCrossing(
                curve,
                Math.Min(historyStart, time),
                time,
                threshold,
                speed > 0f));
        }

        public static double GetCumulativeScroll(
            IReadOnlyList<ScrollPoint> curve,
            double time)
        {
            if (curve == null || curve.Count == 0)
                return time;

            var index = FindPointIndex(curve, time);
            var point = curve[index];
            return point.Cumulative + point.Multiplier * (time - point.Time);
        }

        public static float GetMultiplier(
            IReadOnlyList<ScrollPoint> curve,
            double time)
        {
            if (curve == null || curve.Count == 0)
                return 1f;
            return curve[FindPointIndex(curve, time)].Multiplier;
        }

        public static float GetScrollProgress(
            IReadOnlyList<ScrollPoint> curve,
            double startTime,
            double duration,
            double currentTime)
        {
            if (duration <= Epsilon)
                return 1f;
            var endTime = startTime + duration;
            var evaluatedTime = Math.Max(
                startTime,
                Math.Min(endTime, currentTime));
            var startScroll = GetCumulativeScroll(curve, startTime);
            var currentScroll =
                GetCumulativeScroll(curve, evaluatedTime) - startScroll;
            var totalScroll =
                GetCumulativeScroll(curve, endTime) - startScroll;
            // A positive net integral is normalized so local acceleration,
            // pauses, and rewinds preserve the authored Slide duration. A
            // non-positive integral cannot be normalized without turning
            // backwards motion into forwards motion, so it remains on the
            // base-duration axis and is cut off at the authored end.
            var denominator = totalScroll > Epsilon
                ? totalScroll
                : duration;
            var progress = currentScroll / denominator;
            return (float)Math.Max(0d, Math.Min(1d, progress));
        }

        public static double FindFirstVisibleTime(
            IReadOnlyList<ScrollPoint> curve,
            double searchStart,
            double judgeTime,
            double noteScroll,
            float speed,
            float spawnRadius,
            float destroyRadius)
        {
            if (judgeTime < searchStart)
                return double.NaN;
            if (Math.Abs(speed) <= Epsilon)
                return searchStart;

            var threshold = noteScroll -
                (GetPathLength(spawnRadius, destroyRadius) + SpawnScaleDistance) /
                speed;
            return FindFirstThresholdCrossing(
                curve, searchStart, judgeTime, threshold, speed > 0f);
        }

        /// <summary>
        /// Normalized distance by which the first visible TouchSlide bar centre
        /// leads the guide star, so that bar's trailing edge meets the star.
        /// </summary>
        public static float GetTouchSlideTrailLead(
            float totalPathLength,
            float actualBarSpacing) =>
            totalPathLength <= Epsilon
                ? 0f
                : Math.Max(0f, actualBarSpacing) * 0.5f / totalPathLength;

        public static float GetTouchMotionDuration(float speed) =>
            3.209385682f *
            (float)Math.Pow(Math.Max(Math.Abs(speed), 0.0001f), -0.9549621752f);

        public static float GetTouchVisualTiming(
            double noteScroll,
            double currentScroll,
            float speed) =>
            Math.Abs(speed) <= Epsilon
                ? 0f
                : Math.Sign(speed) * (float)(currentScroll - noteScroll);

        public static double FindFirstTouchVisibleTime(
            IReadOnlyList<ScrollPoint> curve,
            double searchStart,
            double judgeTime,
            double noteScroll,
            float speed)
        {
            if (judgeTime < searchStart)
                return double.NaN;
            if (Math.Abs(speed) <= Epsilon)
                return searchStart;

            var duration = GetTouchMotionDuration(speed);
            var threshold = noteScroll - Math.Sign(speed) * duration;
            return FindFirstThresholdCrossing(
                curve, searchStart, judgeTime, threshold, speed > 0f);
        }

        public static double FindLastTimeAtCumulativeScroll(
            IReadOnlyList<ScrollPoint> curve,
            double endTime,
            double targetScroll)
        {
            if (curve == null || curve.Count == 0)
                return endTime - (endTime - targetScroll);

            var candidate = double.NaN;
            var first = curve[0];
            if (Math.Abs(first.Multiplier) > Epsilon)
            {
                var extrapolated = first.Time +
                    (targetScroll - first.Cumulative) / first.Multiplier;
                if (extrapolated <= Math.Min(first.Time, endTime) + Epsilon)
                    candidate = extrapolated;
            }
            else if (Math.Abs(targetScroll - first.Cumulative) <= Epsilon &&
                     endTime <= first.Time)
            {
                candidate = endTime;
            }

            for (var index = 0; index < curve.Count; index++)
            {
                var start = curve[index].Time;
                if (start > endTime + Epsilon)
                    break;
                var segmentEnd = index + 1 < curve.Count
                    ? Math.Min(endTime, curve[index + 1].Time)
                    : endTime;
                if (segmentEnd < start - Epsilon)
                    continue;

                var point = curve[index];
                var endScroll = point.Cumulative +
                    point.Multiplier * (segmentEnd - start);
                if (Math.Abs(point.Multiplier) <= Epsilon)
                {
                    if (Math.Abs(point.Cumulative - targetScroll) <= Epsilon)
                        candidate = segmentEnd;
                    continue;
                }

                var minimum = Math.Min(point.Cumulative, endScroll) - Epsilon;
                var maximum = Math.Max(point.Cumulative, endScroll) + Epsilon;
                if (targetScroll < minimum || targetScroll > maximum)
                    continue;
                candidate = Math.Min(
                    endTime,
                    start + (targetScroll - point.Cumulative) / point.Multiplier);
            }

            return candidate;
        }

        public static double FindFirstTimeAtCumulativeScroll(
            IReadOnlyList<ScrollPoint> curve,
            double searchStart,
            double endTime,
            double targetScroll,
            bool atOrAbove) =>
            FindFirstThresholdCrossing(
                curve, searchStart, endTime, targetScroll, atOrAbove);

        private static double FindFirstThresholdCrossing(
            IReadOnlyList<ScrollPoint> curve,
            double searchStart,
            double searchEnd,
            double threshold,
            bool atOrAbove)
        {
            var currentTime = searchStart;
            var currentScroll = GetCumulativeScroll(curve, currentTime);
            if (Satisfies(currentScroll, threshold, atOrAbove))
                return currentTime;

            while (currentTime < searchEnd - Epsilon)
            {
                var multiplier = GetMultiplier(curve, currentTime);
                var nextTime = GetNextPointTime(curve, currentTime, searchEnd);
                if (nextTime <= currentTime + Epsilon)
                    nextTime = searchEnd;
                var nextScroll = currentScroll +
                    multiplier * (nextTime - currentTime);
                if (!Satisfies(currentScroll, threshold, atOrAbove) &&
                    Satisfies(nextScroll, threshold, atOrAbove) &&
                    Math.Abs(multiplier) > Epsilon)
                {
                    return currentTime +
                        (threshold - currentScroll) / multiplier;
                }

                currentTime = nextTime;
                currentScroll = nextScroll;
            }

            return double.NaN;
        }

        private static bool Satisfies(
            double value,
            double threshold,
            bool atOrAbove) =>
            atOrAbove
                ? value >= threshold - Epsilon
                : value <= threshold + Epsilon;

        private static int FindPointIndex(
            IReadOnlyList<ScrollPoint> curve,
            double time)
        {
            var low = 0;
            var high = curve.Count - 1;
            while (low < high)
            {
                var middle = low + (high - low + 1) / 2;
                if (curve[middle].Time <= time)
                    low = middle;
                else
                    high = middle - 1;
            }
            return low;
        }

        private static double GetNextPointTime(
            IReadOnlyList<ScrollPoint> curve,
            double time,
            double fallback)
        {
            if (curve == null || curve.Count == 0)
                return fallback;
            var index = FindPointIndex(curve, time);
            if (index + 1 < curve.Count &&
                curve[index + 1].Time > time + Epsilon)
                return Math.Min(fallback, curve[index + 1].Time);
            return fallback;
        }
    }
}
