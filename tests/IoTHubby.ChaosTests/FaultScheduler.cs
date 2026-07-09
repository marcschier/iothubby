// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby.ChaosTests;

internal sealed class FaultScheduler
{
    private static readonly string[] Faults =
    [
        "proxy-drop",
        "proxy-refuse",
        "proxy-blackhole",
        "proxy-latency",
        "broker-disconnect",
        "broker-restart",
    ];

    private readonly BrokerHarness _broker;
    private readonly IReadOnlyList<string> _clientIds;
    private readonly Action<string> _log;
    private readonly ChaosProxy _proxy;
    private readonly Random _random;
    private int _healthy;

    public FaultScheduler(
        ChaosProxy proxy,
        BrokerHarness broker,
        IReadOnlyList<string> clientIds,
        Random random,
        Action<string> log)
    {
        _proxy = proxy;
        _broker = broker;
        _clientIds = clientIds;
        _random = random;
        _log = log;
    }

    public bool IsHealthy => Volatile.Read(ref _healthy) != 0;

    public int FaultsApplied { get; private set; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await HealthyWindowAsync(NextSeconds(10, 16), cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            var fault = Faults[_random.Next(Faults.Length)];
            try
            {
                await ApplyFaultAsync(fault, cancellationToken).ConfigureAwait(false);
                FaultsApplied++;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"fault {fault} raised {ex.GetType().Name}: {ex.Message}");
            }

            await UnhealthyDelayAsync(NextSeconds(16, 26), cancellationToken).ConfigureAwait(false);
            await HealthyWindowAsync(NextSeconds(10, 20), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyFaultAsync(string fault, CancellationToken cancellationToken)
    {
        Volatile.Write(ref _healthy, 0);
        var hold = NextSeconds(2, 7);
        _log($"injecting {fault} (hold {hold:n0}s)");

        switch (fault)
        {
            case "proxy-drop":
                _proxy.DropAllConnections();
                break;
            case "proxy-refuse":
                _proxy.RefuseConnections = true;
                _proxy.ConnectDropRate = 0.75;
                _proxy.DropAllConnections();
                await Task.Delay(TimeSpan.FromSeconds(hold), cancellationToken).ConfigureAwait(false);
                _proxy.ConnectDropRate = 0;
                _proxy.RefuseConnections = false;
                break;
            case "proxy-blackhole":
                _proxy.BlackHole = true;
                await Task.Delay(TimeSpan.FromSeconds(hold), cancellationToken).ConfigureAwait(false);
                _proxy.BlackHole = false;
                _proxy.DropAllConnections();
                break;
            case "proxy-latency":
                _proxy.LatencyMs = _random.Next(100, 700);
                _proxy.LatencyJitterMs = _random.Next(25, 250);
                await Task.Delay(TimeSpan.FromSeconds(hold), cancellationToken).ConfigureAwait(false);
                _proxy.LatencyMs = 0;
                _proxy.LatencyJitterMs = 0;
                break;
            case "broker-disconnect":
                await DisconnectRandomClientAsync().ConfigureAwait(false);
                break;
            case "broker-restart":
                _proxy.DropAllConnections();
                await _broker.RestartAsync().ConfigureAwait(false);
                break;
        }
    }

    private async Task DisconnectRandomClientAsync()
    {
        if (_clientIds.Count == 0)
        {
            return;
        }

        var clientId = _clientIds[_random.Next(_clientIds.Count)];
        await _broker.DisconnectClientAsync(clientId).ConfigureAwait(false);
    }

    private async Task HealthyWindowAsync(double seconds, CancellationToken cancellationToken)
    {
        Volatile.Write(ref _healthy, 1);
        _log($"healthy window {seconds:n0}s");
        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
    }

    private async Task UnhealthyDelayAsync(double seconds, CancellationToken cancellationToken)
    {
        Volatile.Write(ref _healthy, 0);
        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
    }

    private double NextSeconds(int minInclusive, int maxExclusive)
        => _random.Next(minInclusive, maxExclusive) + _random.NextDouble();
}
