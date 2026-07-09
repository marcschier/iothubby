// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace IoTHubby.ChaosTests;

internal sealed record MetricsSnapshot(
    double ElapsedSeconds,
    long TelemetrySuccess,
    long TwinGetSuccess,
    long TwinUpdateSuccess,
    long FakeHubTelemetryReceived,
    int FaultsApplied);

internal sealed class MetricsCollector
{
    private readonly List<MetricsSnapshot> _snapshots = new();

    public void Snapshot(double elapsedSeconds, Workload workload, FakeIoTHub hub, FaultScheduler scheduler)
    {
        _snapshots.Add(new MetricsSnapshot(
            elapsedSeconds,
            workload.TotalTelemetry,
            workload.TotalTwinGets,
            workload.TotalTwinUpdates,
            hub.TelemetryReceived,
            scheduler.FaultsApplied));
    }

    public void WriteCsv(string path)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine(
            "ElapsedSeconds,TelemetrySuccess,TwinGetSuccess,TwinUpdateSuccess,"
            + "FakeHubTelemetryReceived,FaultsApplied");

        foreach (var snapshot in _snapshots)
        {
            writer.Write(snapshot.ElapsedSeconds.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(snapshot.TelemetrySuccess.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(snapshot.TwinGetSuccess.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(snapshot.TwinUpdateSuccess.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(snapshot.FakeHubTelemetryReceived.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.WriteLine(snapshot.FaultsApplied.ToString(CultureInfo.InvariantCulture));
        }
    }
}
