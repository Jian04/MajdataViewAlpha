using System.Numerics;

namespace MajdataEdit;

// Spectral-flux onset envelopes + local autocorrelation. No model/runtime downloads.
// The stages follow the standard onset/correlation beat-tracking approach described
// in https://librosa.org/doc/0.11.0/generated/librosa.beat.beat_track.html .
internal static class AudioRhythmDetector
{
    internal record Tempo(double Start, double End, double Bpm, double Confidence);
    internal record Result(double Duration, IReadOnlyList<Tempo> Tempos, double[][] Onsets);
    internal static readonly string[] Bands = { "全频", "低频 · 鼓点 / 低音", "中频 · 主体", "高频 · 镲片 / 高频" };
    const int Rate = 22050, Size = 2048, Hop = 220;
    const double Step = (double)Hop / Rate;

    internal static Result Analyze(float[] mono, int sampleRate, IProgress<double>? progress, CancellationToken cancel)
    {
        var count = Math.Max(0, (int)((double)mono.Length * Rate / sampleRate));
        var frames = Math.Max(0, (count - Size) / Hop + 1);
        if (frames < 100) throw new InvalidOperationException("音频太短，无法估计节奏。");
        var envelopes = Enumerable.Range(0, 4).Select(_ => new double[frames]).ToArray();
        var previous = new double[Size / 2];
        var spectrum = new Complex[Size];
        var window = Enumerable.Range(0, Size).Select(i => .5 - .5 * Math.Cos(2 * Math.PI * i / (Size - 1))).ToArray();
        for (var frame = 0; frame < frames; frame++)
        {
            cancel.ThrowIfCancellationRequested();
            for (var i = 0; i < Size; i++)
            {
                var source = (frame * Hop + i) * (double)sampleRate / Rate;
                var index = (int)source;
                var value = mono[index] + (mono[Math.Min(index + 1, mono.Length - 1)] - mono[index]) * (source - index);
                spectrum[i] = new Complex(value * window[i], 0);
            }
            Fft(spectrum);
            for (var bin = 3; bin < Size / 2; bin++)
            {
                var magnitude = Math.Log(1 + spectrum[bin].Magnitude * 8);
                var flux = Math.Max(0, magnitude - previous[bin]);
                previous[bin] = magnitude;
                var frequency = (double)bin * Rate / Size;
                var band = frequency < 200 ? 1 : frequency < 1800 ? 2 : 3;
                envelopes[band][frame] += flux;
            }
            if (frame % 100 == 0) progress?.Report(.15 + .65 * frame / frames);
        }
        for (var b = 1; b < 4; b++)
        {
            var max = envelopes[b].DefaultIfEmpty(0).Max();
            if (max > 0) for (var i = 0; i < frames; i++) envelopes[b][i] /= max;
            for (var i = 0; i < frames; i++) envelopes[0][i] += envelopes[b][i] / 3;
        }
        var onsets = envelopes.Select(Peaks).ToArray();
        var duration = (double)mono.Length / sampleRate;
        var segments = new List<Tempo>();
        for (double start = 0; start < duration; start += 2)
        {
            cancel.ThrowIfCancellationRequested();
            var center = start + 1;
            var left = Math.Max(0, (int)((center - 4) / Step));
            var right = Math.Min(frames, (int)((center + 4) / Step));
            var (bpm, confidence) = Estimate(envelopes[0], left, right);
            var end = Math.Min(duration, start + 2);
            if (segments.Count > 0 && Math.Abs(segments[^1].Bpm - bpm) < Math.Max(1.5, bpm * .015))
            {
                var old = segments[^1];
                var weight = old.End - old.Start;
                segments[^1] = old with { End = end, Bpm = (old.Bpm * weight + bpm * (end - start)) / (weight + end - start), Confidence = Math.Min(old.Confidence, confidence) };
            }
            else segments.Add(new Tempo(start, end, bpm, confidence));
        }
        progress?.Report(1);
        return new Result(duration, segments, onsets);
    }

    static double[] Peaks(double[] envelope)
    {
        var result = new List<double>();
        double last = -1;
        for (var i = 2; i < envelope.Length - 2; i++)
        {
            var start = Math.Max(0, i - 10); var end = Math.Min(envelope.Length, i + 11);
            double mean = 0; for (var k = start; k < end; k++) mean += envelope[k];
            mean /= end - start;
            if (envelope[i] > .018 && envelope[i] > mean * 1.5 && envelope[i] >= envelope[i - 1] &&
                envelope[i] > envelope[i + 1] && i * Step - last >= .06)
            {
                // Flux timestamps belong to the analysis window's centre.
                last = i * Step; result.Add(Math.Max(0, last + Size / (2d * Rate)));
            }
        }
        return result.ToArray();
    }

    static (double Bpm, double Confidence) Estimate(double[] envelope, int start, int end)
    {
        var min = (int)(60 / 240d / Step); var max = (int)(60 / 60d / Step);
        var correlations = new double[max + 2];
        for (var lag = min - 1; lag <= max + 1; lag++)
        {
            double value = 0, a = 0, b = 0;
            for (var i = start + lag; i < end; i++)
            {
                value += envelope[i] * envelope[i - lag];
                a += envelope[i] * envelope[i]; b += envelope[i - lag] * envelope[i - lag];
            }
            correlations[lag] = value / Math.Max(1e-12, Math.Sqrt(a * b));
        }
        var best = min; double score = -1;
        var localPeaks = Peaks(envelope.Skip(start).Take(end - start).ToArray());
        var intervals = localPeaks.Zip(localPeaks.Skip(1), (a,b) => b-a).Where(x=>x>.1&&x<1.5).OrderBy(x=>x).ToArray();
        var observedPeriod = intervals.Length > 3 ? intervals[intervals.Length/2] / Step : 0;
        while(observedPeriod > 0 && observedPeriod < min) observedPeriod *= 2;
        while(observedPeriod > max) observedPeriod /= 2;
        for (var lag = min; lag <= max; lag++)
        {
            var bpm = 60 / (lag * Step);
            // A soft prior breaks exact double/half-tempo ties; expose confidence for review.
            var current = correlations[lag] * (.85 + .15 * Math.Exp(-Math.Pow(Math.Log(bpm / 130), 2) / .45));
            if(observedPeriod > 0) current *= .8 + .2 * Math.Exp(-Math.Pow(Math.Log(lag / observedPeriod), 2) / .02);
            if (current > score) { best = lag; score = current; }
        }
        var denominator = correlations[best - 1] - 2 * correlations[best] + correlations[best + 1];
        var fraction = Math.Abs(denominator) > 1e-8 ? Math.Clamp(.5 * (correlations[best - 1] - correlations[best + 1]) / denominator, -.5, .5) : 0;
        return (60 / ((best + fraction) * Step), Math.Clamp(correlations[best], 0, 1));
    }

    static void Fft(Complex[] data)
    {
        for (int i = 1, j = 0; i < data.Length; i++)
        {
            var bit = data.Length >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }
        for (var length = 2; length <= data.Length; length <<= 1)
        {
            var rotation = Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
            for (var start = 0; start < data.Length; start += length)
            {
                var factor = Complex.One;
                for (var j = 0; j < length / 2; j++)
                {
                    var a = data[start + j]; var b = data[start + j + length / 2] * factor;
                    data[start + j] = a + b; data[start + j + length / 2] = a - b; factor *= rotation;
                }
            }
        }
    }
}
