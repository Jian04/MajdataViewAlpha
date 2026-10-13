using System;
using System.Collections.Generic;
using System.Numerics;

namespace MajdataCore
{
    // Group consecutive guide markers by their nearest physical sensor, as
    // AstroDX's RegularSlideSegmentHandler does for its slide arrows. Work in
    // chart-local coordinates so window/layout transforms cannot change areas.
    public static class TouchSlideSensorRoute
    {
        // Pure D-key routes use the D/E/C sensor layout; mixed A/D routes must
        // retain both layouts, including an explicitly authored middle key.
        public static bool AllowsKeyPathGroup(char group, bool startD, bool endD, bool mixedMiddle = false)
            => mixedMiddle || startD != endD || (startD ? group is not ('A' or 'B') : group is not ('D' or 'E'));

        public readonly struct Zone
        {
            public readonly int Sensor;
            public readonly float Entry;

            public Zone(int sensor, float entry)
            {
                Sensor = sensor;
                Entry = entry;
            }
        }

        public static List<Zone> Build(
            IReadOnlyList<Vector2> path,
            IReadOnlyList<(int Sensor, Vector2 Center)> sensors,
            float markerSpacing)
        {
            var result = new List<Zone>();
            if (path.Count < 2 || sensors.Count == 0 || markerSpacing <= 0)
                return result;

            var distances = new float[path.Count];
            for (var i = 1; i < path.Count; i++)
                distances[i] = distances[i - 1] + Vector2.Distance(path[i - 1], path[i]);
            var length = Math.Max(0.0001f, distances[path.Count - 1]);

            var segment = 1;
            for (var distance = markerSpacing; distance <= length + 0.0001f; distance += markerSpacing)
            {
                while (segment < path.Count - 1 && distances[segment] < distance)
                    segment++;
                var before = Math.Max(0, segment - 1);
                var span = distances[segment] - distances[before];
                var fraction = span <= 0.0001f ? 0f :
                    Math.Max(0f, Math.Min(1f, (distance - distances[before]) / span));
                var point = Vector2.Lerp(path[before], path[segment], fraction);
                var nearest = sensors[0].Sensor;
                var best = Vector2.DistanceSquared(point, sensors[0].Center);
                for (var j = 1; j < sensors.Count; j++)
                {
                    var separation = Vector2.DistanceSquared(point, sensors[j].Center);
                    if (separation < best)
                    {
                        best = separation;
                        nearest = sensors[j].Sensor;
                    }
                }
                if (result.Count == 0 || result[result.Count - 1].Sensor != nearest)
                    result.Add(new Zone(nearest, result.Count == 0 ? 0f : Math.Min(1f, distance / length)));
            }
            return result;
        }
    }
}
