// Copyright (c) marcschier. Licensed under the MIT License.

using Mqtt.Client.Testing;

namespace IoTHubby.ChaosTests;

internal sealed class BrokerHarness : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MqttTestBroker _broker;

    private BrokerHarness(MqttTestBroker broker)
    {
        _broker = broker;
        Port = broker.Port;
    }

    public int Port { get; }

    public static async Task<BrokerHarness> StartAsync()
    {
        var broker = await MqttTestBroker.StartAsync().ConfigureAwait(false);
        return new BrokerHarness(broker);
    }

    public async Task RestartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _broker.DisposeAsync().ConfigureAwait(false);
            _broker = await MqttTestBroker.StartAsync(new MqttTestBrokerOptions { Port = Port })
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisconnectClientAsync(string clientId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _broker.DisconnectClient(clientId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _broker.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
