// Sensor thresholds and transition table adapted from TeamMajdata/MajdataPlay (GPL-3.0).
// Source: c3423a4bba536e53921e8fdedab2b9d91121b393, SlideDataBuilder.cs.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace MajdataCore
{
    public readonly struct SlideAreaRawData
    {
        public readonly double LengthAfterPush;
        public readonly double LengthAfterFinish;
        public readonly int SensorA;
        public readonly int SensorB;

        public SlideAreaRawData(double push, double finish, int sensorA, int sensorB = -1)
        {
            LengthAfterPush = push;
            LengthAfterFinish = finish;
            SensorA = sensorA;
            SensorB = sensorB;
        }
    }

    public static class SlideCodeJudgment
    {
        private static readonly Dictionary<int, SlideAreaRawData[]> SlideAreaLookup = new();
        private static readonly Complex[] CentersA = new Complex[8];
        private static readonly Complex[] CentersB = new Complex[8];

        static SlideCodeJudgment()
        {
            for (var i = 0; i < 8; i++)
            {
                var angle = Math.PI * (3d / 8d - i / 4d);
                CentersA[i] = Complex.FromPolarCoordinates(HitAreaADistance, angle);
                CentersB[i] = Complex.FromPolarCoordinates(HitAreaBDistance, angle);
                for (var j = 0; j < 8; j++)
                    AddSlideAreaLookupEntries(i, j);
            }
        }

        private static int? FindNode(Complex point)
        {
            if (SquaredMagnitude(point) < HitAreaCRadius * HitAreaCRadius)
                return 16;
            for (var j = 0; j < 8; j++)
            {
                if (SquaredMagnitude(point - CentersA[j]) < HitAreaARadius * HitAreaARadius)
                    return j;
                if (SquaredMagnitude(point - CentersB[j]) < HitAreaBRadius * HitAreaBRadius)
                    return j | 8;
            }
            return null;
        }

        private static double SquaredMagnitude(Complex point) =>
            point.Real * point.Real + point.Imaginary * point.Imaginary;

        private static void AddSlideAreaLookupEntries(int i, int j)
        {
            var diff = (j - i) & 7; 
            int tmp, tmp2;
            var key = (i << 5) | j;
            switch (diff)
            {
                case 1:
                case 7:
                    {
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.32, 0.68, i),
                        new SlideAreaRawData(1.00, 1.00, j)
                    };
                        break;
                    }
                case 2:
                case 6:
                    {
                        tmp = (diff == 2) ? (i + 1) & 7 : (i - 1) & 7;
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.20, 0.38, i),
                        new SlideAreaRawData(0.62, 0.80, tmp, tmp | 8),
                        new SlideAreaRawData(1.00, 1.00, j)
                    };
                        break;
                    }
            }
            key = ((i | 8) << 5) | (j | 8);
            switch (diff)
            {
                case 1:
                case 7:
                    {
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.35, 0.65, i | 8),
                        new SlideAreaRawData(1.00, 1.00, j | 8)
                    };
                        break;
                    }
                case 2:
                case 6:
                    {
                        tmp = (diff == 2) ? (i + 1) & 7 : (i - 1) & 7;
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.22, 0.40, i | 8),
                        new SlideAreaRawData(0.60, 0.78, tmp | 8, 16),
                        new SlideAreaRawData(1.00, 1.00, j | 8)
                    };
                        break;
                    }
                case 3:
                case 5:
                    {
                        tmp = (diff == 3) ? (i + 1) & 7 : (i - 1) & 7;
                        tmp2 = (diff == 3) ? (i + 2) & 7 : (i - 2) & 7;
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.15, 0.28, i | 8),
                        new SlideAreaRawData(0.43, 0.57, tmp | 8, 16),
                        new SlideAreaRawData(0.72, 0.85, tmp2 | 8, 16),
                        new SlideAreaRawData(1.00, 1.00, j | 8)
                    };
                        break;
                    }
            }
            key = (i << 5) | (j | 8);
            var key2 = ((j | 8) << 5) | i;
            switch (diff)
            {
                case 0:
                    {
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.50, 0.80, i),
                        new SlideAreaRawData(1.00, 1.00, j | 8)
                    };
                        SlideAreaLookup[key2] = new[]
                        {
                        new SlideAreaRawData(0.20, 0.50, j | 8),
                        new SlideAreaRawData(1.00, 1.00, i)
                    };
                        break;
                    }
                case 1:
                case 7:
                    {
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.45, 0.77, i),
                        new SlideAreaRawData(1.00, 1.00, j | 8)
                    };
                        SlideAreaLookup[key2] =
                            new[]
                            {
                            new SlideAreaRawData(0.23, 0.55, j | 8),
                            new SlideAreaRawData(1.00, 1.00, i)
                            };
                        break;
                    }
                case 3:
                case 5:
                    {
                        tmp = (diff == 3) ? (i + 1) & 7 : (i - 1) & 7;
                        tmp2 = (diff == 3) ? (i + 2) & 7 : (i - 2) & 7;
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.20, 0.34, i),
                        new SlideAreaRawData(0.54, 0.67, i | 8, tmp | 8),
                        new SlideAreaRawData(0.80, 0.90, tmp2 | 8, 16),
                        new SlideAreaRawData(1.00, 1.00, j | 8)
                    };
                        SlideAreaLookup[key2] = new[]
                        {
                        new SlideAreaRawData(0.10, 0.20, j | 8),
                        new SlideAreaRawData(0.33, 0.46, tmp2 | 8, 16),
                        new SlideAreaRawData(0.66, 0.80, i | 8, tmp | 8),
                        new SlideAreaRawData(1.00, 1.00, i)
                    };
                        break;
                    }
            }
            key = (16 << 5) | (j | 8);
            key2 = ((j | 8) << 5) | 16;
            SlideAreaLookup[key] = new[]
            {
                new SlideAreaRawData(0.40, 0.70, 16),
                new SlideAreaRawData(1.00, 1.00, j | 8)
            };
            SlideAreaLookup[key2] = new[]
            {
                new SlideAreaRawData(0.30, 0.60, j | 8),
                new SlideAreaRawData(1.00, 1.00, 16)
            };
        }
        public const double HitAreaCalcStep = 4.8 / 48.0;

        public const double HitAreaARadius = 4.8 * 80.0 / 480.0;
        public const double HitAreaADistance = 4.8 * 440.0 / 480.0;
        public const double HitAreaBRadius = 4.8 * 45.0 / 480.0;
        public const double HitAreaBDistance = 4.8 * 210.0 / 480.0;
        public const double HitAreaCRadius = 4.8 * 55.0 / 480.0;

        public const double LastDistanceCircle = 4.8 * 175.0 / 480.0;
        public const double LastDistanceShort = 4.8 * 130.0 / 480.0;
        public const double LastDistanceLong = 4.8 * 159.0 / 480.0;
        public static SlideAreaRawData[] Build(double totalLength, Func<double, Complex> pointAt, bool endsWithLine)
        {

            var nodeList = new List<(int, double)>(); 
            var count = Math.Max(1, (int)Math.Round(totalLength / HitAreaCalcStep));

            int? lastNode = null;
            var enterLength = 0.0;

            for (var i = 0; i < count; i++)
            {
                var t = (double)i / count;
                var pt = pointAt(t);
                var node = FindNode(pt);

                if (lastNode != node)
                {
                    var length = t * totalLength;
                    if (lastNode == null)
                    {
                        enterLength = length;
                    }
                    else
                    {
                        nodeList.Add((lastNode.Value, (length + enterLength) / 2.0));

                        if (node != null)
                        {
                            enterLength = length;
                        }
                    }
                }

                lastNode = node;
            }
            var endNode = FindNode(pointAt(1d));
            if (!endNode.HasValue || nodeList.Count == 0)
                throw new InvalidOperationException("SlideCode route must connect A/B/C sensor regions.");
            nodeList.Add((endNode.Value, totalLength));
            nodeList[0] = (nodeList[0].Item1, 0.0);
            var result = new List<SlideAreaRawData>();
            result.Add(new SlideAreaRawData(0.0, 0.0, nodeList[0].Item1));

            for (var i = 1; i < nodeList.Count; i++)
            {
                var key = (nodeList[i - 1].Item1 << 5) | nodeList[i].Item1;
                var lastLength = nodeList[i - 1].Item2;
                var segmentLength = nodeList[i].Item2 - lastLength;

                if (nodeList[i - 1].Item1 == nodeList[i].Item1)
                    continue;
                if (!SlideAreaLookup.TryGetValue(key, out var data))
                    throw new InvalidOperationException(
                        $"SlideCode route skips sensor regions: {nodeList[i - 1].Item1} -> {nodeList[i].Item1}.");
                var area = result[^1];
                result[^1] = new SlideAreaRawData(
                    lastLength + segmentLength * data[0].LengthAfterPush,
                    lastLength + segmentLength * data[0].LengthAfterFinish,
                    area.SensorA, area.SensorB
                );
                for (var j = 1; j < data.Length; j++)
                {
                    result.Add(new SlideAreaRawData(
                        lastLength + segmentLength * data[j].LengthAfterPush,
                        lastLength + segmentLength * data[j].LengthAfterFinish,
                        data[j].SensorA, data[j].SensorB
                    ));
                }
            }

            if (result.Count < 2)
                throw new InvalidOperationException("SlideCode route needs at least two judgement areas.");
            double lastDistance;

            if (endsWithLine)
            {
                lastDistance = nodeList[^2].Item1 <= 7 ? LastDistanceShort : LastDistanceLong;
            }
            else
            {
                lastDistance = LastDistanceCircle;
            }
            var last2ndArea = result[^2];
            var lastArea = result[^1];
            result[^2] = new SlideAreaRawData(last2ndArea.LengthAfterPush, totalLength - lastDistance,
                last2ndArea.SensorA, last2ndArea.SensorB);
            result[^1] = new SlideAreaRawData(totalLength, totalLength, lastArea.SensorA, lastArea.SensorB);

            return result.ToArray();
        }
    }
}
