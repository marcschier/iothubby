// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;

namespace IoTHubby.ChaosTests;

internal sealed record LeakSample(
    double ElapsedSeconds,
    long ManagedHeapBytes,
    long WorkingSetBytes,
    int ThreadCount);

internal sealed class LeakDetector
{
    private const long HeapAbsoluteSlackBytes = 64L * 1024L * 1024L;
    private const double HeapGrowthFactor = 3.0;
    private const double MaxHeapSlopeBytesPerSecond = 1024.0 * 1024.0 / 30.0;
    private const double ThreadFactor = 3.0;
    private const int ThreadSlack = 64;

    private readonly List<LeakSample> _samples = new();
    private readonly TimeSpan _warmup;

    public LeakDetector(TimeSpan warmup) => _warmup = warmup;

    public IReadOnlyList<LeakSample> Samples => new ReadOnlyCollection<LeakSample>(_samples);

    public LeakSample Sample(double elapsedSeconds)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var process = Process.GetCurrentProcess();
        process.Refresh();

        var sample = new LeakSample(
            elapsedSeconds,
            GC.GetTotalMemory(forceFullCollection: true),
            process.WorkingSet64,
            process.Threads.Count);
        _samples.Add(sample);
        return sample;
    }

    public void WriteCsv(string path)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("ElapsedSeconds,ManagedHeapBytes,WorkingSetBytes,ThreadCount");

        foreach (var sample in _samples)
        {
            writer.Write(sample.ElapsedSeconds.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(sample.ManagedHeapBytes.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(sample.WorkingSetBytes.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.WriteLine(sample.ThreadCount.ToString(CultureInfo.InvariantCulture));
        }
    }

    public bool TryDetectLeak(out string reason)
    {
        var postWarmup = _samples
            .Where(sample => sample.ElapsedSeconds >= _warmup.TotalSeconds)
            .ToArray();

        if (postWarmup.Length < 3)
        {
            reason = "Not enough post-warmup samples to detect a leak.";
            return false;
        }

        var baseline = postWarmup[0];
        var maxHeapBytes = postWarmup.Max(static sample => sample.ManagedHeapBytes);
        var maxThreadCount = postWarmup.Max(static sample => sample.ThreadCount);
        var heapLimit = (baseline.ManagedHeapBytes * HeapGrowthFactor) + HeapAbsoluteSlackBytes;
        var threadLimit = (baseline.ThreadCount * ThreadFactor) + ThreadSlack;

        if (maxHeapBytes > heapLimit)
        {
            reason = $"Managed heap grew from {baseline.ManagedHeapBytes} to {maxHeapBytes}, "
                + $"above limit {heapLimit:F0}.";
            return true;
        }

        if (maxThreadCount > threadLimit)
        {
            reason = $"Thread count grew from {baseline.ThreadCount} to {maxThreadCount}, "
                + $"above limit {threadLimit:F0}.";
            return true;
        }

        var heapSlope = CalculateHeapSlope(postWarmup);
        if (heapSlope > MaxHeapSlopeBytesPerSecond)
        {
            reason = $"Managed heap slope was {heapSlope:F2} bytes/sec, "
                + $"above limit {MaxHeapSlopeBytesPerSecond:F2}.";
            return true;
        }

        reason = "No leak detected.";
        return false;
    }

    private static double CalculateHeapSlope(IReadOnlyList<LeakSample> samples)
    {
        var count = samples.Count;
        var sumX = 0.0;
        var sumY = 0.0;
        var sumXY = 0.0;
        var sumXX = 0.0;

        foreach (var sample in samples)
        {
            var x = sample.ElapsedSeconds;
            var y = sample.ManagedHeapBytes;
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumXX += x * x;
        }

        var denominator = (count * sumXX) - (sumX * sumX);
        if (Math.Abs(denominator) < double.Epsilon)
        {
            return 0.0;
        }

        return ((count * sumXY) - (sumX * sumY)) / denominator;
    }
}
