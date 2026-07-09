// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using Mqtt.Client;

namespace IoTHubby.ChaosTests;

internal sealed class ChaosWorker
{
    private readonly IoTHubDeviceClient _client;
    private readonly Action<string> _log;
    private int _recoverableErrorsLogged;
    private long _lastProgressTicks;

    public ChaosWorker(string id, int proxyPort, Action<string> log)
    {
        Id = id;
        _log = log;
        _lastProgressTicks = Stopwatch.GetTimestamp();
        _client = IoTHubDeviceClient.CreateFromConnectionString(
            "HostName=test-hub.azure-devices.net;DeviceId=" + id + ";SharedAccessKey=aGVsbG8=",
            options =>
            {
                options.EndpointHostOverride = "127.0.0.1";
                options.EndpointPortOverride = proxyPort;
                options.DisableTls = true;
                options.KeepAlive = TimeSpan.FromSeconds(3);
                options.OperationTimeout = TimeSpan.FromSeconds(10);
                options.ReconnectInitialDelay = TimeSpan.FromMilliseconds(250);
                options.ReconnectMaxDelay = TimeSpan.FromSeconds(2);
                options.ReceiveChannelCapacity = 256;
            });
    }

    public string Id { get; }

    public long TelemetrySuccess { get; private set; }

    public long TwinGetSuccess { get; private set; }

    public long TwinUpdateSuccess { get; private set; }

    public Exception? Fault { get; private set; }

    public string? LastRecoverableError { get; private set; }

    public IoTHubConnectionState State => _client.State;

    public double SecondsSinceProgress
        => (Stopwatch.GetTimestamp() - Volatile.Read(ref _lastProgressTicks)) * 1.0 / Stopwatch.Frequency;

    public long Progress => TelemetrySuccess + TwinGetSuccess + TwinUpdateSuccess;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ConnectWithRetryAsync(cancellationToken).ConfigureAwait(false);
            var sequence = 0L;

            while (!cancellationToken.IsCancellationRequested)
            {
                await SendTelemetryAsync(sequence, cancellationToken).ConfigureAwait(false);
                if (sequence % 10 == 0)
                {
                    await GetTwinAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (sequence % 10 == 5)
                {
                    await UpdateTwinAsync(sequence, cancellationToken).ConfigureAwait(false);
                }

                sequence++;
                await Task.Delay(TimeSpan.FromMilliseconds(75), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Fault = ex;
            _log($"worker {Id} faulted: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                operation.CancelAfter(TimeSpan.FromSeconds(15));
                await _client.ConnectAsync(operation.Token).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsRecoverable(ex) && !cancellationToken.IsCancellationRequested)
            {
                RecordRecoverable("connect", ex);
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task SendTelemetryAsync(long sequence, CancellationToken cancellationToken)
    {
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            operation.CancelAfter(TimeSpan.FromSeconds(8));
            var message = TelemetryMessage.FromString(
                string.Create(CultureInfo.InvariantCulture, $"{{\"worker\":\"{Id}\",\"seq\":{sequence}}}"));
            message.MessageId = string.Create(CultureInfo.InvariantCulture, $"{Id}-{sequence}");
            await _client.SendTelemetryAsync(message, operation.Token).ConfigureAwait(false);
            TelemetrySuccess++;
            MarkProgress();
        }
        catch (Exception ex) when (IsRecoverable(ex) && !cancellationToken.IsCancellationRequested)
        {
            RecordRecoverable("telemetry", ex);
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task GetTwinAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            operation.CancelAfter(TimeSpan.FromSeconds(10));
            _ = await _client.GetTwinAsync(operation.Token).ConfigureAwait(false);
            TwinGetSuccess++;
            MarkProgress();
        }
        catch (Exception ex) when (IsRecoverable(ex) && !cancellationToken.IsCancellationRequested)
        {
            RecordRecoverable("twin", ex);
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task UpdateTwinAsync(long sequence, CancellationToken cancellationToken)
    {
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            operation.CancelAfter(TimeSpan.FromSeconds(10));
            var patch = string.Create(
                CultureInfo.InvariantCulture,
                $"{{\"chaos\":{{\"worker\":\"{Id}\",\"seq\":{sequence}}}}}");
            _ = await _client.UpdateReportedPropertiesAsync(patch, operation.Token).ConfigureAwait(false);
            TwinUpdateSuccess++;
            MarkProgress();
        }
        catch (Exception ex) when (IsRecoverable(ex) && !cancellationToken.IsCancellationRequested)
        {
            RecordRecoverable("twin", ex);
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsRecoverable(Exception ex)
        => ex is IOException
            or TimeoutException
            or OperationCanceledException
            or IoTHubClientException
            or MqttConnectionException
            or MqttProtocolException
            or InvalidOperationException;

    private void MarkProgress()
        => Volatile.Write(ref _lastProgressTicks, Stopwatch.GetTimestamp());

    private void RecordRecoverable(string operation, Exception exception)
    {
        LastRecoverableError = $"{operation}: {exception.GetType().Name}: {exception.Message}";
        if (Interlocked.Increment(ref _recoverableErrorsLogged) <= 5)
        {
            _log($"worker {Id} recoverable {LastRecoverableError}");
        }
    }
}

internal sealed class Workload
{
    private readonly List<Task> _tasks = new();
    private readonly List<ChaosWorker> _workers = new();

    public IReadOnlyList<ChaosWorker> Workers => _workers;

    public IReadOnlyList<string> ClientIds => _workers.Select(static worker => worker.Id).ToArray();

    public long TotalTelemetry => Sum(static worker => worker.TelemetrySuccess);

    public long TotalTwinGets => Sum(static worker => worker.TwinGetSuccess);

    public long TotalTwinUpdates => Sum(static worker => worker.TwinUpdateSuccess);

    public long TotalProgress => Sum(static worker => worker.Progress);

    public void Start(int count, int proxyPort, Action<string> log, CancellationToken cancellationToken)
    {
        for (var i = 0; i < count; i++)
        {
            var worker = new ChaosWorker($"chaos-device-{i}", proxyPort, log);
            _workers.Add(worker);
            _tasks.Add(Task.Run(() => worker.RunAsync(cancellationToken), cancellationToken));
        }
    }

    public Task WhenAllAsync() => Task.WhenAll(_tasks);

    public bool CheckLiveness(double maxStallSeconds, out string reason)
    {
        foreach (var worker in _workers)
        {
            if (worker.Fault is not null)
            {
                reason = $"worker {worker.Id} faulted: {worker.Fault.GetType().Name}: {worker.Fault.Message}";
                return false;
            }

            if (worker.SecondsSinceProgress > maxStallSeconds)
            {
                reason = $"worker {worker.Id} stalled {worker.SecondsSinceProgress:n1}s "
                    + $"(> {maxStallSeconds:n0}s) during a healthy window [state={worker.State}]";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private long Sum(Func<ChaosWorker, long> selector)
    {
        long total = 0;
        foreach (var worker in _workers)
        {
            total += selector(worker);
        }

        return total;
    }
}
