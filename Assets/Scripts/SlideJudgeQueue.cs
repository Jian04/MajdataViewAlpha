using System;
using System.Collections.Generic;
using Assets.Scripts.Types;

// MajdataPlay SlideBase.SensorCheck: current/next areas are checked together,
// and all areas completed by the same input sample are consumed in order.
internal static class SlideJudgeQueue
{
    // SlideDrop checks the current area and its immediate neighbour once per
    // sensor sample. A single contact must not drain every later area that is
    // already On (the guide star may overlap several sensors at once).
    public static int AdvanceOne(IReadOnlyList<JudgeArea> areas, int cursor,
        Func<SensorType, SensorStatus> status)
    {
        if (cursor >= areas.Count)
            return cursor;

        void Poll(JudgeArea area)
        {
            foreach (var sensor in area.GetSensorTypes()) area.Judge(sensor, status(sensor));
        }

        var first = areas[cursor];
        Poll(first);
        if (cursor + 1 < areas.Count && (first.CanSkip || first.On))
        {
            var second = areas[cursor + 1];
            Poll(second);
            if (second.IsFinished) return cursor + 2;
            if (second.On) return cursor + 1;
        }
        return first.IsFinished ? cursor + 1 : cursor;
    }

    public static int Advance(IReadOnlyList<JudgeArea> areas, int cursor,
        Func<SensorType, SensorStatus> status)
    {
        while (cursor < areas.Count)
        {
            var next = AdvanceOne(areas, cursor, status);
            if (next == cursor) break;
            cursor = next;
        }
        return cursor;
    }
}
