// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;

namespace IoTHubby.ChaosTests;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var config = ChaosConfig.Parse(args);
        var stopwatch = Stopwatch.StartNew();
        void Log(string message) => Console.WriteLine($"[{stopwatch.Elapsed:hh\\:mm\\:ss}] {message}");

        Console.WriteLine("=== IoTHubby chaos / soak ===");
        Console.WriteLine($"config: {config}");
        Console.WriteLine($"*** SEED={config.Seed} (re-pass --seed {config.Seed} to reproduce) ***");

        var rootRandom = new Random(config.Seed);
        await using var broker = await BrokerHarness.StartAsync().ConfigureAwait(false);
        Log($"broker listening on 127.0.0.1:{broker.Port}");

        await using var hub = await FakeIoTHub.StartAsync(broker.Port).ConfigureAwait(false);
        Log("fake IoT Hub connected directly to broker");

        await using var proxy = new ChaosProxy(
            new ChaosProxyOptions { BrokerPort = broker.Port },
            new Random(rootRandom.Next()));
        proxy.Start();
        Log($"chaos proxy listening on 127.0.0.1:{proxy.ListenPort} -> broker {broker.Port}");

        var warmup = TimeSpan.FromSeconds(Math.Min(60, Math.Max(15, config.Duration.TotalSeconds / 4)));
        var leakDetector = new LeakDetector(warmup);
        var metrics = new MetricsCollector();
        var workload = new Workload();

        using var runCts = new CancellationTokenSource(config.Duration);
        workload.Start(config.Clients, proxy.ListenPort, Log, runCts.Token);

        var scheduler = new FaultScheduler(
            proxy,
            broker,
            workload.ClientIds,
            new Random(rootRandom.Next()),
            Log);
        var schedulerTask = scheduler.RunAsync(runCts.Token);
        var violations = new List<string>();

        leakDetector.Sample(0);
        await MonitorAsync(
            config,
            stopwatch,
            workload,
            hub,
            scheduler,
            metrics,
            leakDetector,
            violations,
            runCts).ConfigureAwait(false);

        Log("soak window elapsed; draining workers");
        if (!runCts.IsCancellationRequested)
        {
            await runCts.CancelAsync();
        }

        await AwaitCancellationAsync(schedulerTask).ConfigureAwait(false);
        await AwaitCancellationAsync(workload.WhenAllAsync()).ConfigureAwait(false);

        metrics.Snapshot(stopwatch.Elapsed.TotalSeconds, workload, hub, scheduler);
        leakDetector.Sample(stopwatch.Elapsed.TotalSeconds);
        AddFinalViolations(config, workload, scheduler, leakDetector, violations);

        var report = WriteReport(config, stopwatch, workload, hub, scheduler, metrics, leakDetector, violations);
        PrintSummary(stopwatch, config, workload, hub, scheduler, report, violations);
        return violations.Count == 0 ? 0 : 1;
    }

    private static async Task MonitorAsync(
        ChaosConfig config,
        Stopwatch stopwatch,
        Workload workload,
        FakeIoTHub hub,
        FaultScheduler scheduler,
        MetricsCollector metrics,
        LeakDetector leakDetector,
        List<string> violations,
        CancellationTokenSource runCts)
    {
        const double MonitorIntervalSeconds = 2;
        const double LeakIntervalSeconds = 10;
        const double WatchdogGraceSeconds = 15;
        const double MaxStallSeconds = 30;

        double? healthySince = null;
        var lastLeakSample = 0.0;

        try
        {
            while (!runCts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(MonitorIntervalSeconds), runCts.Token)
                    .ConfigureAwait(false);
                var elapsed = stopwatch.Elapsed.TotalSeconds;
                metrics.Snapshot(elapsed, workload, hub, scheduler);

                if (elapsed - lastLeakSample >= LeakIntervalSeconds)
                {
                    leakDetector.Sample(elapsed);
                    lastLeakSample = elapsed;
                }

                if (scheduler.IsHealthy)
                {
                    healthySince ??= elapsed;
                    if (elapsed - healthySince.Value >= WatchdogGraceSeconds
                        && !workload.CheckLiveness(MaxStallSeconds, out var reason))
                    {
                        violations.Add(reason);
                        await runCts.CancelAsync();
                    }
                }
                else
                {
                    healthySince = null;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void AddFinalViolations(
        ChaosConfig config,
        Workload workload,
        FaultScheduler scheduler,
        LeakDetector leakDetector,
        List<string> violations)
    {
        foreach (var worker in workload.Workers)
        {
            if (worker.Fault is not null)
            {
                violations.Add(
                    $"worker {worker.Id} faulted: {worker.Fault.GetType().Name}: {worker.Fault.Message}");
            }

            if (worker.TelemetrySuccess == 0)
            {
                violations.Add($"worker {worker.Id} sent no telemetry");
            }

            if (worker.TwinGetSuccess == 0 || worker.TwinUpdateSuccess == 0)
            {
                violations.Add($"worker {worker.Id} completed no twin round-trip");
            }
        }

        if (config.Duration >= TimeSpan.FromSeconds(45) && scheduler.FaultsApplied == 0)
        {
            violations.Add("no faults were applied during the run");
        }

        if (leakDetector.TryDetectLeak(out var leakReason))
        {
            violations.Add($"resource leak: {leakReason}");
        }
    }

    private static string WriteReport(
        ChaosConfig config,
        Stopwatch stopwatch,
        Workload workload,
        FakeIoTHub hub,
        FaultScheduler scheduler,
        MetricsCollector metrics,
        LeakDetector leakDetector,
        IReadOnlyList<string> violations)
    {
        Directory.CreateDirectory(config.ReportDir);
        var metricsCsv = Path.Combine(config.ReportDir, "chaos-metrics.csv");
        var memoryCsv = Path.Combine(config.ReportDir, "chaos-memory.csv");
        var summaryPath = Path.Combine(config.ReportDir, "summary.txt");

        metrics.WriteCsv(metricsCsv);
        leakDetector.WriteCsv(memoryCsv);

        using var writer = new StreamWriter(summaryPath);
        writer.WriteLine($"duration={stopwatch.Elapsed}");
        writer.WriteLine($"seed={config.Seed}");
        writer.WriteLine($"faults={scheduler.FaultsApplied}");
        writer.WriteLine($"telemetry={workload.TotalTelemetry}");
        writer.WriteLine($"fakeHubTelemetry={hub.TelemetryReceived}");
        writer.WriteLine($"twinGets={workload.TotalTwinGets}");
        writer.WriteLine($"twinUpdates={workload.TotalTwinUpdates}");
        writer.WriteLine($"violations={violations.Count}");
        foreach (var violation in violations)
        {
            writer.WriteLine($"- {violation}");
        }

        return config.ReportDir;
    }

    private static void PrintSummary(
        Stopwatch stopwatch,
        ChaosConfig config,
        Workload workload,
        FakeIoTHub hub,
        FaultScheduler scheduler,
        string reportDir,
        IReadOnlyList<string> violations)
    {
        Console.WriteLine();
        Console.WriteLine("=== summary ===");
        Console.WriteLine($"duration          : {stopwatch.Elapsed}");
        Console.WriteLine($"seed              : {config.Seed}");
        Console.WriteLine($"faults applied    : {scheduler.FaultsApplied}");
        Console.WriteLine($"telemetry sent    : {workload.TotalTelemetry}");
        Console.WriteLine($"telemetry captured: {hub.TelemetryReceived}");
        Console.WriteLine($"twin gets         : {workload.TotalTwinGets}");
        Console.WriteLine($"twin updates      : {workload.TotalTwinUpdates}");
        Console.WriteLine($"report dir        : {reportDir}");
        Console.WriteLine();

        if (violations.Count == 0)
        {
            Console.WriteLine("IOTHUBBY CHAOS SOAK PASSED");
            return;
        }

        Console.WriteLine($"IOTHUBBY CHAOS SOAK FAILED with {violations.Count} violation(s):");
        foreach (var violation in violations)
        {
            Console.WriteLine($"  - {violation}");
        }
    }

    private static async Task AwaitCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
