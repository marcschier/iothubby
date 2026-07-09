// Copyright (c) marcschier. Licensed under the MIT License.

using Mqtt.Client.Testing;

namespace IoTHubby.IntegrationTests;

/// <summary>
/// Bundles an in-process <see cref="MqttTestBroker"/> and a <see cref="FakeIoTHub"/> and builds
/// device/module clients wired to the broker over plain TCP via the internal test seam.
/// </summary>
internal sealed class TestHost : IAsyncDisposable
{
    private TestHost(MqttTestBroker broker, FakeIoTHub hub)
    {
        Broker = broker;
        Hub = hub;
    }

    public MqttTestBroker Broker { get; }

    public FakeIoTHub Hub { get; }

    public static async Task<TestHost> StartAsync()
    {
        var broker = await MqttTestBroker.StartAsync();
        var hub = await FakeIoTHub.StartAsync(broker.Port);
        return new TestHost(broker, hub);
    }

    public IoTHubDeviceClient CreateDevice(string deviceId = "dev1", Action<IoTHubClientOptions>? configure = null)
        => IoTHubDeviceClient.CreateFromConnectionString(
            $"HostName=test-hub.azure-devices.net;DeviceId={deviceId};SharedAccessKey=aGVsbG8=",
            o =>
            {
                Wire(o);
                configure?.Invoke(o);
            });

    public IoTHubModuleClient CreateModule(
        string deviceId = "dev1", string moduleId = "mod1", Action<IoTHubClientOptions>? configure = null)
        => IoTHubModuleClient.CreateFromConnectionString(
            $"HostName=test-hub.azure-devices.net;DeviceId={deviceId};ModuleId={moduleId};SharedAccessKey=aGVsbG8=",
            o =>
            {
                Wire(o);
                configure?.Invoke(o);
            });

    private void Wire(IoTHubClientOptions o)
    {
        o.EndpointHostOverride = "127.0.0.1";
        o.EndpointPortOverride = Broker.Port;
        o.DisableTls = true;
    }

    public async ValueTask DisposeAsync()
    {
        await Hub.DisposeAsync();
        await Broker.DisposeAsync();
    }
}
