using System;

namespace MajdataCore
{
    public static class TouchSlideDirection
    {
        // SimaiSharp uses zero-based keys: (index + 2) % 8 >= 4.
        public static bool IsCounterClockwise(int startPosition, char shape)
        {
            var lowerHalf = (startPosition + 1) % 8 >= 4;
            return shape == '>' ? lowerHalf : !lowerHalf;
        }

        public static double Sweep(double start, double end, int startPosition, char shape)
        {
            const double turn = Math.PI * 2d;
            var ccw = (end - start) % turn;
            if (ccw < 0d)
                ccw += turn;
            var sameAngle = ccw < 0.00001d || turn - ccw < 0.00001d;
            if (shape == '^')
                return sameAngle ? 0d : ccw <= Math.PI ? ccw : ccw - turn;
            var direction = IsCounterClockwise(startPosition, shape) ? 1d : -1d;
            return sameAngle ? direction * turn : direction > 0d ? ccw : ccw - turn;
        }
    }
}
