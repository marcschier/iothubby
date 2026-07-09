// Copyright (c) marcschier. Licensed under the MIT License.

using IoTHubby.Provisioning;
using Mqtt.Client.Testing;

namespace IoTHubby.IntegrationTests;

public sealed class CoverageIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Telemetry_with_all_system_properties()
    {
        await using var host = await TestHost.StartAsync();
        await using var device = host.CreateDevice("dev-props");
        await device.ConnectAsync();

        var message = new TelemetryMessage("body"u8.ToArray().AsMemory())
        {
            MessageId = "m1",
            CorrelationId = "c1",
            UserId = "u1",
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            MessageSchema = "schema1",
            CreationTimeUtc = DateTimeOffset.UnixEpoch,
            ExpiryTimeUtc = DateTimeOffset.UnixEpoch.AddHours(1),
            QoS = IoTHubQoS.AtLeastOnce,
        };
        message.Properties["app1"] = "v1";

        await device.SendTelemetryAsync(message);
        var captured = await host.Hub.WaitForTelemetryAsync(Timeout);

        await Assert.That(captured.Topic).Contains("%24.mid=m1");
        await Assert.That(captured.Topic).Contains("%24.cid=c1");
        await Assert.That(captured.Topic).Contains("%24.uid=u1");
        await Assert.That(captured.Topic).Contains("%24.ce=utf-8");
        await Assert.That(captured.Topic).Contains("%24.schema=schema1");
        await Assert.That(captured.Topic).Contains("app1=v1");
    }

    [Test]
    public async Task Module_receives_cloud_to_device_and_updates_reported_variants()
    {
        await using var host = await TestHost.StartAsync();
        await using var module = host.CreateModule("dev-mc", "modR");
        await module.ConnectAsync();

        await Assert.That(await module.UpdateReportedPropertiesAsync("{\"a\":1}")).IsNotNull();
        await Assert.That(await module.UpdateReportedPropertiesAsync("{\"b\":2}"u8.ToArray().AsMemory())).IsNotNull();

        using var cts = new CancellationTokenSource(Timeout);
        var receive = Task.Run(async () =>
        {
            await foreach (var msg in module.ReceiveCloudToDeviceMessagesAsync(cts.Token))
            {
                return msg.PayloadAsString;
            }
            return null;
        });

        string? got = null;
        while (!cts.IsCancellationRequested)
        {
            await host.Hub.PublishC2dAsync("dev-mc", "to-module");
            var done = await Task.WhenAny(receive, Task.Delay(300, cts.Token));
            if (done == receive)
            {
                got = await receive;
                break;
            }
        }
        await Assert.That(got).IsEqualTo("to-module");
    }

    [Test]
    public async Task Dps_registration_times_out_without_a_responder()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        // No FakeDps started, so the register request is never answered.
        await using var client = ProvisioningClient.CreateWithSymmetricKey(
            "0ne00000000",
            "reg-timeout",
            "aGVsbG8=",
            o =>
            {
                o.EndpointHostOverride = "127.0.0.1";
                o.EndpointPortOverride = broker.Port;
                o.DisableTls = true;
                o.Timeout = TimeSpan.FromSeconds(3);
            });

        await Assert.That(async () => await client.RegisterAsync()).Throws<IoTHubClientException>();
    }
}
