// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using Microsoft.Extensions.DependencyInjection;

namespace IoTHubby.IntegrationTests;

public sealed class SequenceAndDiIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Multi_segment_telemetry_arrives_concatenated()
    {
        await using var host = await TestHost.StartAsync();
        await using var device = host.CreateDevice("dev-seq");
        await device.ConnectAsync();

        var seq = MultiSegment("{\"part1\":\"a\","u8.ToArray(), "\"part2\":\"b\"}"u8.ToArray());
        await device.SendTelemetryAsync(new TelemetryMessage(seq));

        var captured = await host.Hub.WaitForTelemetryAsync(Timeout);
        await Assert.That(captured.PayloadString).IsEqualTo("{\"part1\":\"a\",\"part2\":\"b\"}");
    }

    [Test]
    public async Task Di_built_device_client_connects()
    {
        await using var host = await TestHost.StartAsync();
        var services = new ServiceCollection();
        services.AddIoTHubDeviceClient(
            "HostName=test-hub.azure-devices.net;DeviceId=dev-di;SharedAccessKey=aGVsbG8=",
            o =>
            {
                o.EndpointHostOverride = "127.0.0.1";
                o.EndpointPortOverride = host.Broker.Port;
                o.DisableTls = true;
            });
        await using var provider = services.BuildServiceProvider();

        var device = provider.GetRequiredService<IoTHubDeviceClient>();
        await device.ConnectAsync();
        await Assert.That(device.State).IsEqualTo(IoTHubConnectionState.Connected);

        await device.SendTelemetryAsync(TelemetryMessage.FromString("{\"di\":true}"));
        var captured = await host.Hub.WaitForTelemetryAsync(Timeout);
        await Assert.That(captured.Topic).StartsWith("devices/dev-di/messages/events/");
    }

    private static ReadOnlySequence<byte> MultiSegment(params byte[][] chunks)
    {
        var first = new Segment(chunks[0]);
        var last = first;
        for (var i = 1; i < chunks.Length; i++)
        {
            last = last.Append(chunks[i]);
        }
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
