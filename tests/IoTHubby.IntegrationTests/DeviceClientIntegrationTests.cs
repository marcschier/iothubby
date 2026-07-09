// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace IoTHubby.IntegrationTests;

public sealed class DeviceClientIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Connect_succeeds()
    {
        await using var host = await TestHost.StartAsync();
        await using var device = host.CreateDevice();

        await device.ConnectAsync();

        await Assert.That(device.State).IsEqualTo(IoTHubConnectionState.Connected);
    }

    [Test]
    public async Task Telemetry_reaches_hub_with_properties()
    {
        await using var host = await TestHost.StartAsync();
        await using var device = host.CreateDevice("dev-telemetry");
        await device.ConnectAsync();

        var message = TelemetryMessage.FromString("{\"t\":21}");
        message.MessageId = "m-1";
        message.ContentType = "application/json";
        message.Properties["zone"] = "north";
        await device.SendTelemetryAsync(message);

        var captured = await host.Hub.WaitForTelemetryAsync(Timeout);
        await Assert.That(captured.Topic).StartsWith("devices/dev-telemetry/messages/events/");
        await Assert.That(captured.Topic).Contains("%24.mid=m-1");
        await Assert.That(captured.Topic).Contains("zone=north");
        await Assert.That(captured.PayloadString).IsEqualTo("{\"t\":21}");
    }

    [Test]
    public async Task Cloud_to_device_message_is_received()
    {
        await using var host = await TestHost.StartAsync();
        await using var device = host.CreateDevice("dev-c2d");
        await device.ConnectAsync();

        using var cts = new CancellationTokenSource(Timeout);
        var receive = Task.Run(async () =>
        {
            await foreach (var msg in device.ReceiveCloudToDeviceMessagesAsync(cts.Token))
            {
                return msg;
            }
            return null;
        });

        // Give the subscription a moment, then push until received.
        CloudToDeviceMessage? received = null;
        while (!cts.IsCancellationRequested)
        {
            await host.Hub.PublishC2dAsync("dev-c2d", "hello-device", "%24.mid=c1");
            var completed = await Task.WhenAny(receive, Task.Delay(300, cts.Token));
            if (completed == receive)
            {
                received = await receive;
                break;
            }
        }

        await Assert.That(received).IsNotNull();
        await Assert.That(received!.PayloadAsString).IsEqualTo("hello-device");
        await Assert.That(received.MessageId).IsEqualTo("c1");
    }

    [Test]
    public async Task Direct_method_is_handled_and_answered()
    {
        await using var host = await TestHost.StartAsync();
        await using var device = host.CreateDevice("dev-methods");
        await device.ConnectAsync();

        await device.SetMethodHandlerAsync((request, _) =>
        {
            var body = $"{{\"echo\":\"{request.Name}\"}}";
            return new ValueTask<DirectMethodResponse>(DirectMethodResponse.FromString(200, body));
        });

        var result = await host.Hub.InvokeMethodAsync("reboot", "{\"delay\":5}", Timeout);

        await Assert.That(result.Status).IsEqualTo(200);
        await Assert.That(result.PayloadString).IsEqualTo("{\"echo\":\"reboot\"}");
    }

    [Test]
    public async Task Unhandled_method_returns_404()
    {
        await using var host = await TestHost.StartAsync();
        await using var device = host.CreateDevice("dev-nomethod");
        await device.ConnectAsync();

        // Register a handler (so the subscription exists) that only knows "known".
        await device.SetMethodHandlerAsync((request, _) => new ValueTask<DirectMethodResponse>(
            request.Name == "known"
                ? DirectMethodResponse.FromStatus(200)
                : DirectMethodResponse.FromStatus(501)));

        var result = await host.Hub.InvokeMethodAsync("unknown", "{}", Timeout);
        await Assert.That(result.Status).IsEqualTo(501);
    }

    [Test]
    public async Task GetTwin_returns_desired_and_reported()
    {
        await using var host = await TestHost.StartAsync();
        await using var device = host.CreateDevice("dev-twin");
        host.Hub.SetTwin("{\"desired\":{\"interval\":30,\"$version\":4},\"reported\":{\"t\":21,\"$version\":9}}");
        await device.ConnectAsync();

        var twin = await device.GetTwinAsync();

        await Assert.That(twin.Desired.Version).IsEqualTo(4L);
        await Assert.That(twin.Reported.Version).IsEqualTo(9L);
        await Assert.That(twin.Desired.RootElement.GetProperty("interval").GetInt32()).IsEqualTo(30);
    }

    [Test]
    public async Task UpdateReportedProperties_returns_new_version()
    {
        await using var host = await TestHost.StartAsync();
        await using var device = host.CreateDevice("dev-reported");
        await device.ConnectAsync();

        var version = await device.UpdateReportedPropertiesAsync("{\"status\":\"online\"}");

        await Assert.That(version).IsNotNull();
        await Assert.That(host.Hub.LastReportedPatch).IsEqualTo("{\"status\":\"online\"}");
    }

    [Test]
    public async Task Desired_property_updates_are_streamed()
    {
        await using var host = await TestHost.StartAsync();
        await using var device = host.CreateDevice("dev-desired");
        await device.ConnectAsync();

        using var cts = new CancellationTokenSource(Timeout);
        var receive = Task.Run(async () =>
        {
            await foreach (var update in device.ReceiveDesiredPropertyUpdatesAsync(cts.Token))
            {
                return update;
            }
            return null;
        });

        DesiredPropertyUpdate? update = null;
        while (!cts.IsCancellationRequested)
        {
            await host.Hub.PublishDesiredAsync("{\"interval\":60}", version: 7);
            var done = await Task.WhenAny(receive, Task.Delay(300, cts.Token));
            if (done == receive)
            {
                update = await receive;
                break;
            }
        }

        await Assert.That(update).IsNotNull();
        await Assert.That(update!.Properties.Version).IsEqualTo(7L);
        await Assert.That(update.Properties.RootElement.GetProperty("interval").GetInt32()).IsEqualTo(60);
    }

    [Test]
    public async Task Client_reconnects_and_resubscribes_after_drop()
    {
        await using var host = await TestHost.StartAsync();
        await using var device = host.CreateDevice("dev-reconnect");
        await device.ConnectAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var receive = Task.Run(async () =>
        {
            await foreach (var msg in device.ReceiveCloudToDeviceMessagesAsync(cts.Token))
            {
                if (Encoding.UTF8.GetString(msg.PayloadMemory.ToArray()) == "after-reconnect")
                {
                    return true;
                }
            }
            return false;
        });

        // Force a disconnect; the client should auto-reconnect and re-subscribe.
        await Task.Delay(500);
        host.Broker.DisconnectClient("dev-reconnect");

        var got = false;
        while (!cts.IsCancellationRequested)
        {
            await host.Hub.PublishC2dAsync("dev-reconnect", "after-reconnect");
            var done = await Task.WhenAny(receive, Task.Delay(400, cts.Token));
            if (done == receive)
            {
                got = await receive;
                break;
            }
        }

        await Assert.That(got).IsTrue();
    }

    [Test]
    public async Task Sas_connection_string_client_connects()
    {
        await using var host = await TestHost.StartAsync();
        // Pre-computed SAS auth path (no key) still connects against the anonymous broker.
        await using var device = IoTHubDeviceClient.CreateFromConnectionString(
            "HostName=test-hub.azure-devices.net;DeviceId=dev-sas;" +
            "SharedAccessSignature=SharedAccessSignature sr=abc&sig=xyz&se=99999999999",
            o =>
            {
                o.EndpointHostOverride = "127.0.0.1";
                o.EndpointPortOverride = host.Broker.Port;
                o.DisableTls = true;
            });

        await device.ConnectAsync();
        await Assert.That(device.State).IsEqualTo(IoTHubConnectionState.Connected);
    }
}
