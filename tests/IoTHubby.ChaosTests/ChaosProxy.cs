// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace IoTHubby.ChaosTests;

internal sealed class ChaosProxyOptions
{
    public string BrokerHost { get; set; } = "127.0.0.1";

    public int BrokerPort { get; set; }
}

internal sealed class ChaosProxy : IAsyncDisposable
{
    private const int BufferSize = 16 * 1024;
    private const int BlackHoleDelayMs = 100;

    private readonly ConcurrentDictionary<ProxyConnection, byte> _connections = new();
    private readonly TcpListener _listener;
    private readonly object _randomGate = new();
    private readonly ChaosProxyOptions _options;
    private readonly Random _random;
    private readonly CancellationTokenSource _stop = new();
    private int _blackHole;
    private int _latencyJitterMs;
    private int _latencyMs;
    private int _refuseConnections;
    private int _started;
    private long _connectDropRateBits;
    private Task? _acceptTask;

    public ChaosProxy(ChaosProxyOptions options, Random random)
    {
        _options = options;
        _random = random;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        ListenPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public int ListenPort { get; }

    public bool BlackHole
    {
        get => Volatile.Read(ref _blackHole) != 0;
        set => Volatile.Write(ref _blackHole, value ? 1 : 0);
    }

    public bool RefuseConnections
    {
        get => Volatile.Read(ref _refuseConnections) != 0;
        set => Volatile.Write(ref _refuseConnections, value ? 1 : 0);
    }

    public int LatencyMs
    {
        get => Volatile.Read(ref _latencyMs);
        set => Volatile.Write(ref _latencyMs, Math.Max(0, value));
    }

    public int LatencyJitterMs
    {
        get => Volatile.Read(ref _latencyJitterMs);
        set => Volatile.Write(ref _latencyJitterMs, Math.Max(0, value));
    }

    public double ConnectDropRate
    {
        get => BitConverter.Int64BitsToDouble(Volatile.Read(ref _connectDropRateBits));
        set => Volatile.Write(ref _connectDropRateBits, BitConverter.DoubleToInt64Bits(Math.Clamp(value, 0, 1)));
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _acceptTask = AcceptLoopAsync(_stop.Token);
    }

    public void DropAllConnections()
    {
        foreach (var connection in _connections.Keys)
        {
            if (_connections.TryRemove(connection, out _))
            {
                connection.ResetAndDispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stop.IsCancellationRequested)
        {
            await _stop.CancelAsync();
        }

        _listener.Stop();
        DropAllConnections();

        if (_acceptTask is not null)
        {
            await IgnoreConnectionErrorsAsync(_acceptTask).ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    private static async Task IgnoreConnectionErrorsAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpectedConnectionError(ex))
        {
        }
    }

    private static bool IsExpectedConnectionError(Exception ex)
        => ex is IOException or ObjectDisposedException or OperationCanceledException or SocketException;

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;

            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                if (RefuseConnections || Hits(ConnectDropRate))
                {
                    client.Dispose();
                    continue;
                }

                _ = HandleClientAsync(client, cancellationToken);
                client = null;
            }
            catch (Exception ex) when (IsExpectedConnectionError(ex))
            {
                client?.Dispose();
                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken stopToken)
    {
        var broker = new TcpClient();

        try
        {
            client.NoDelay = true;
            await broker.ConnectAsync(_options.BrokerHost, _options.BrokerPort, stopToken).ConfigureAwait(false);
            broker.NoDelay = true;
        }
        catch (Exception ex) when (IsExpectedConnectionError(ex))
        {
            client.Dispose();
            broker.Dispose();
            return;
        }

        var connection = new ProxyConnection(client, broker);
        if (!_connections.TryAdd(connection, 0))
        {
            connection.Dispose();
            return;
        }

        await using var registration = stopToken.Register(connection.ResetAndDispose);
        var clientPump = PumpAsync(connection, client.GetStream(), broker.GetStream(), stopToken);
        var brokerPump = PumpAsync(connection, broker.GetStream(), client.GetStream(), stopToken);

        await Task.WhenAny(clientPump, brokerPump).ConfigureAwait(false);
        RemoveConnection(connection);
        await IgnoreConnectionErrorsAsync(Task.WhenAll(clientPump, brokerPump)).ConfigureAwait(false);
    }

    private async Task PumpAsync(
        ProxyConnection connection,
        NetworkStream source,
        NetworkStream target,
        CancellationToken stopToken)
    {
        var buffer = new byte[BufferSize];

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(connection.Token, stopToken);
            while (!linked.Token.IsCancellationRequested)
            {
                var read = await source.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (BlackHole)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(BlackHoleDelayMs), linked.Token).ConfigureAwait(false);
                    continue;
                }

                await DelayForLatencyAsync(linked.Token).ConfigureAwait(false);
                await target.WriteAsync(buffer.AsMemory(0, read), linked.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (IsExpectedConnectionError(ex))
        {
        }
        finally
        {
            RemoveConnection(connection);
        }
    }

    private async Task DelayForLatencyAsync(CancellationToken cancellationToken)
    {
        var delayMs = LatencyMs;
        var jitterMs = LatencyJitterMs;
        if (jitterMs > 0)
        {
            delayMs += Next(-jitterMs, jitterMs + 1);
        }

        delayMs = Math.Max(0, delayMs);
        if (delayMs > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken).ConfigureAwait(false);
        }
    }

    private bool Hits(double probability)
    {
        if (probability <= 0)
        {
            return false;
        }

        lock (_randomGate)
        {
            return _random.NextDouble() < probability;
        }
    }

    private int Next(int minValue, int maxValue)
    {
        lock (_randomGate)
        {
            return _random.Next(minValue, maxValue);
        }
    }

    private void RemoveConnection(ProxyConnection connection)
    {
        if (_connections.TryRemove(connection, out _))
        {
            connection.Dispose();
        }
    }

    private sealed class ProxyConnection : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private int _disposed;

        public ProxyConnection(TcpClient client, TcpClient broker)
        {
            Client = client;
            Broker = broker;
        }

        public TcpClient Client { get; }

        public TcpClient Broker { get; }

        public CancellationToken Token => _cancellation.Token;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _cancellation.Cancel();
            Client.Dispose();
            Broker.Dispose();
            _cancellation.Dispose();
        }

        public void ResetAndDispose()
        {
            SetReset(Client);
            SetReset(Broker);
            Dispose();
        }

        private static void SetReset(TcpClient client)
        {
            try
            {
                client.LingerState = new LingerOption(true, 0);
            }
            catch (Exception ex) when (IsExpectedConnectionError(ex))
            {
            }
            catch (NullReferenceException)
            {
            }
        }
    }
}
