// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby.IntegrationTests;

public sealed class ModuleClientIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Module_telemetry_reaches_hub()
    {
        await using var host = await TestHost.StartAsync();
        await using var module = host.CreateModule("dev-m", "modA");
        await module.ConnectAsync();

        await module.SendTelemetryAsync(TelemetryMessage.FromString("{\"v\":1}"));

        var captured = await host.Hub.WaitForTelemetryAsync(Timeout);
        await Assert.That(captured.Topic).StartsWith("devices/dev-m/modules/modA/messages/events/");
    }

    [Test]
    public async Task Send_to_output_sets_output_name()
    {
        await using var host = await TestHost.StartAsync();
        await using var module = host.CreateModule("dev-m", "modB");
        await module.ConnectAsync();

        await module.SendToOutputAsync("telemetryOut", TelemetryMessage.FromString("{\"x\":2}"));

        var captured = await host.Hub.WaitForTelemetryAsync(Timeout);
        await Assert.That(captured.Topic).StartsWith("devices/dev-m/modules/modB/messages/events/");
        await Assert.That(captured.Topic).Contains("%24.on=telemetryOut");
    }

    [Test]
    public async Task Receive_input_messages_yields_named_input()
    {
        await using var host = await TestHost.StartAsync();
        await using var module = host.CreateModule("dev-m", "modC");
        await module.ConnectAsync();

        using var cts = new CancellationTokenSource(Timeout);
        var receive = Task.Run(async () =>
        {
            await foreach (var msg in module.ReceiveInputMessagesAsync("input1", cts.Token))
            {
                return msg;
            }
            return null;
        });

        CloudToDeviceMessage? received = null;
        while (!cts.IsCancellationRequested)
        {
            await host.Hub.PublishInputAsync("dev-m", "modC", "input1", "input-payload");
            var done = await Task.WhenAny(receive, Task.Delay(300, cts.Token));
            if (done == receive)
            {
                received = await receive;
                break;
            }
        }

        await Assert.That(received).IsNotNull();
        await Assert.That(received!.PayloadAsString).IsEqualTo("input-payload");
        await Assert.That(received.InputName).IsEqualTo("input1");
    }
}
