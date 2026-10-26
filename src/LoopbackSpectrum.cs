using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace IslandPanel;
// Default OUTPUT loopback only. Samples are analyzed in memory; no writer, microphone or network.
public sealed class LoopbackSpectrum : IDisposable
{
    readonly object stateGate = new(), sampleGate = new();
    readonly SpectrumAnalyzer analyzer = new();
    WasapiLoopbackCapture? capture;
    Task work = Task.CompletedTask;
    bool requested, disposed;
    int running;
    long lastAnalysis, lastSamples, version, frames;
    WaveFormat? format;
    bool floatFormat;
    public string? Error { get; private set; }
    public bool LowPower { get; set; }
    public bool Requested { get { lock (stateGate) return requested; } }
    public bool IsRunning => Volatile.Read(ref running) != 0;
    public long AudioFrames => Interlocked.Read(ref frames);
    public Task SetActiveAsync(bool active)
    {
        lock (stateGate)
        {
            if (disposed || requested == active) return work;
            requested = active;
            work = work.ContinueWith(_ => Reconcile(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return work;
        }
    }
    void Reconcile()
    {
        bool active; lock (stateGate) active = requested && !disposed;
        try
        {
            if (!active) { StopCore(); return; }
            if (capture != null) return;
            Error = null; var c = new WasapiLoopbackCapture(); capture = c; format = c.WaveFormat;
            floatFormat = format.Encoding == WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
            if (floatFormat && format.BitsPerSample != 32 || !floatFormat && format.BitsPerSample != 16 && format.BitsPerSample != 24 && format.BitsPerSample != 32) throw new NotSupportedException("Формат устройства не поддерживается");
            lock (sampleGate) { analyzer.Reset(); analyzer.SampleRate = format.SampleRate; lastAnalysis = lastSamples = 0; }
            c.DataAvailable += OnData;
            c.RecordingStopped += OnStopped;
            c.StartRecording(); Volatile.Write(ref running, 1);
        }
        catch (Exception e) { Error = e.Message; StopCore(); }
    }
    void StopCore()
    {
        var c = capture; capture = null; Volatile.Write(ref running, 0);
        if (c != null) { c.DataAvailable -= OnData; c.RecordingStopped -= OnStopped; try { c.StopRecording(); } finally { c.Dispose(); } }
        lock (sampleGate) { analyzer.Reset(); lastSamples = 0; version++; }
    }
    void OnStopped(object? sender, StoppedEventArgs e) { Volatile.Write(ref running, 0); if (e.Exception != null) Error = e.Exception.Message; }
    void OnData(object? sender, WaveInEventArgs e)
    {
        var f = format; if (f == null || e.BytesRecorded == 0) return;
        int stride = f.BlockAlign, bytes = f.BitsPerSample / 8;
        lock (sampleGate)
        {
            // Consume the shared NAudio buffer synchronously before returning from this callback.
            for (int offset = 0; offset + stride <= e.BytesRecorded; offset += stride)
            {
                float sum = 0, loudest = 0;
                for (int ch = 0; ch < f.Channels; ch++)
                {
                    int p = offset + ch * bytes;
                    float v = floatFormat ? BitConverter.ToSingle(e.Buffer, p) : bytes == 2 ? BitConverter.ToInt16(e.Buffer, p) / 32768f : bytes == 3 ? ((e.Buffer[p] | e.Buffer[p + 1] << 8 | e.Buffer[p + 2] << 16) << 8 >> 8) / 8388608f : BitConverter.ToInt32(e.Buffer, p) / 2147483648f;
                    sum += v; if (Math.Abs(v) > Math.Abs(loudest)) loudest = v;
                }
                float mono = sum / f.Channels; if (Math.Abs(mono) < Math.Abs(loudest) * .1f) mono = loudest; analyzer.Add(mono);
            }
            Interlocked.Add(ref frames, e.BytesRecorded / stride);
            long now = Stopwatch.GetTimestamp(); lastSamples = now;
            if ((now - lastAnalysis) * 1000d / Stopwatch.Frequency >= (LowPower ? 75 : 35) && analyzer.Analyze()) { lastAnalysis = now; version++; }
        }
    }
    public long CopyFrame(float[] bands, float[] wave, out float level)
    {
        lock (sampleGate)
        {
            bool fresh = lastSamples != 0 && (Stopwatch.GetTimestamp() - lastSamples) / (double)Stopwatch.Frequency < .4;
            if (fresh) { Array.Copy(analyzer.Bands, bands, SpectrumAnalyzer.BandCount); Array.Copy(analyzer.Wave, wave, SpectrumAnalyzer.WaveCount); level = analyzer.Level; }
            else { Array.Clear(bands); Array.Clear(wave); level = 0; }
            return version;
        }
    }
    public void Dispose()
    {
        Task stop;
        lock (stateGate) { if (disposed) return; disposed = true; requested = false; stop = work = work.ContinueWith(_ => { try { StopCore(); } catch { } }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default); }
        try { stop.Wait(1500); } catch { }
    }
}
