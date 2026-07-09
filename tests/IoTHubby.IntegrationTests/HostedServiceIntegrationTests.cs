// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IoTHubby.IntegrationTests;

public sealed class HostedServiceIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Hosted_service_connects_on_start_and_disconnects_on_stop()
    {
        await using var host = await TestHost.StartAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIoTHubDeviceClient(
            "HostName=test-hub.azure-devices.net;DeviceId=dev-hosted;SharedAccessKey=aGVsbG8=",
            o =>
            {
                o.EndpointHostOverride = "127.0.0.1";
                o.EndpointPortOverride = host.Broker.Port;
                o.DisableTls = true;
            },
            connectOnStart: true);
        await using var provider = services.BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>().Single();
        await hosted.StartAsync(CancellationToken.None);

        var client = provider.GetRequiredService<IoTHubDeviceClient>();
        await Assert.That(client.State).IsEqualTo(IoTHubConnectionState.Connected);

        await hosted.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task Hosted_service_start_swallows_connect_failure()
    {
        // Point at an unused loopback port with reconnect off so the initial connect fails fast.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIoTHubDeviceClient(
            "HostName=test-hub.azure-devices.net;DeviceId=dev-fail;SharedAccessKey=aGVsbG8=",
            o =>
            {
                o.EndpointHostOverride = "127.0.0.1";
                o.EndpointPortOverride = 1; // nothing listening
                o.DisableTls = true;
                o.AutoReconnect = false;
                o.OperationTimeout = TimeSpan.FromSeconds(2);
            },
            connectOnStart: true);
        await using var provider = services.BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>().Single();
        // Must not throw even though the connection cannot be established.
        using var cts = new CancellationTokenSource(Timeout);
        await hosted.StartAsync(cts.Token);
        await hosted.StopAsync(cts.Token);
    }

    [Test]
    public async Task Update_reported_properties_from_sequence()
    {
        await using var host = await TestHost.StartAsync();
        await using var device = host.CreateDevice("dev-rep-seq");
        await device.ConnectAsync();

        var seq = new ReadOnlySequence<byte>("{\"status\":\"online\"}"u8.ToArray());
        var version = await device.UpdateReportedPropertiesAsync(seq);

        await Assert.That(version).IsNotNull();
        await Assert.That(host.Hub.LastReportedPatch).IsEqualTo("{\"status\":\"online\"}");
    }
}
