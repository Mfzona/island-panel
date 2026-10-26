using System;

namespace IslandPanel;
// Fixed-size Hann FFT. All working buffers and twiddles are reused.
public sealed class SpectrumAnalyzer
{
    public const int Size = 2048, BandCount = 32, WaveCount = 128;
    readonly float[] ring = new float[Size];
    readonly double[] real = new double[Size], imaginary = new double[Size], window = new double[Size], cosine = new double[Size / 2], sine = new double[Size / 2];
    readonly int[] reverse = new int[Size];
    public float[] Bands { get; } = new float[BandCount];
    public float[] Wave { get; } = new float[WaveCount];
    public float Level { get; private set; }
    public int SampleRate { get; set; } = 48000;
    int position, filled;
    public SpectrumAnalyzer()
    {
        for (int i = 0; i < Size; i++) { window[i] = .5 - .5 * Math.Cos(2 * Math.PI * i / (Size - 1)); int v = i, r = 0; for (int j = 0; j < 11; j++) { r = (r << 1) | (v & 1); v >>= 1; } reverse[i] = r; }
        for (int i = 0; i < Size / 2; i++) { cosine[i] = Math.Cos(-2 * Math.PI * i / Size); sine[i] = Math.Sin(-2 * Math.PI * i / Size); }
    }
    public void Add(float value) { ring[position] = float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0; position = (position + 1) & (Size - 1); filled = Math.Min(Size, filled + 1); }
    public void Feed(ReadOnlySpan<float> samples) { foreach (var s in samples) Add(s); }
    public bool Analyze()
    {
        if (filled < Size) return false;
        double power = 0;
        for (int i = 0; i < Size; i++) { float v = ring[(position + i) & (Size - 1)]; int r = reverse[i]; real[r] = v * window[i]; imaginary[r] = 0; power += v * v; }
        Level = (float)Math.Sqrt(power / Size);
        for (int length = 2; length <= Size; length <<= 1)
        {
            int half = length / 2, step = Size / length;
            for (int offset = 0; offset < Size; offset += length)
                for (int j = 0; j < half; j++)
                {
                    int a = offset + j, b = a + half, k = j * step;
                    double tr = real[b] * cosine[k] - imaginary[b] * sine[k], ti = real[b] * sine[k] + imaginary[b] * cosine[k];
                    real[b] = real[a] - tr; imaginary[b] = imaginary[a] - ti; real[a] += tr; imaginary[a] += ti;
                }
        }
        double upper = Math.Min(16000, SampleRate * .45);
        for (int band = 0; band < BandCount; band++)
        {
            int lo = Math.Clamp((int)(40 * Math.Pow(upper / 40, band / (double)BandCount) * Size / SampleRate), 1, Size / 2 - 1);
            int hi = Math.Clamp((int)(40 * Math.Pow(upper / 40, (band + 1d) / BandCount) * Size / SampleRate), lo + 1, Size / 2);
            double energy = 0;
            for (int k = lo; k < hi; k++) energy += real[k] * real[k] + imaginary[k] * imaginary[k];
            double magnitude = Math.Sqrt(energy / (hi - lo)) * 4 / Size;
            double db = 20 * Math.Log10(Math.Max(1e-10, magnitude));
            Bands[band] = (float)Math.Pow(Math.Clamp((db + 76) / 66, 0, 1), 1.2);
        }
        for (int i = 0; i < WaveCount; i++) Wave[i] = ring[(position + i * Size / WaveCount) & (Size - 1)];
        return true;
    }
    public void Reset() { Array.Clear(ring); Array.Clear(Bands); Array.Clear(Wave); Level = 0; filled = position = 0; }
}
