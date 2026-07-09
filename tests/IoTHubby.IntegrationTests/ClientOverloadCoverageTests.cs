// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Text.Json.Serialization;

namespace IoTHubby.IntegrationTests;

public sealed class ClientOverloadCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Device_reported_typed_and_sequence_overloads()
    {
        await using var host = await TestHost.StartAsync();
        await using var device = host.CreateDevice("dev-ovl");
        await device.ConnectAsync();

        await Assert.That(await device.UpdateReportedPropertiesAsync(
            new Reported { Battery = 90 }, OverloadJsonContext.Default.Reported)).IsNotNull();
        await Assert.That(await device.UpdateReportedPropertiesAsync(
            new ReadOnlySequence<byte>("{\"c\":3}"u8.ToArray()))).IsNotNull();

        // Register then clear the method handler (null path).
        await device.SetMethodHandlerAsync((_, _) => new ValueTask<DirectMethodResponse>(
            DirectMethodResponse.FromStatus(200)));
        await device.SetMethodHandlerAsync(null);
    }

    [Test]
    public async Task Module_reported_typed_and_desired_stream()
    {
        await using var host = await TestHost.StartAsync();
        await using var module = host.CreateModule("dev-ovm", "modO");
        await module.ConnectAsync();

        await Assert.That(await module.UpdateReportedPropertiesAsync(
            new Reported { Battery = 42 }, OverloadJsonContext.Default.Reported)).IsNotNull();
        await Assert.That(await module.UpdateReportedPropertiesAsync(
            new ReadOnlySequence<byte>("{\"m\":1}"u8.ToArray()))).IsNotNull();

        using var cts = new CancellationTokenSource(Timeout);
        var receive = Task.Run(async () =>
        {
            await foreach (var update in module.ReceiveDesiredPropertyUpdatesAsync(cts.Token))
            {
                return update.Properties.Version;
            }
            return null;
        });

        long? version = null;
        while (!cts.IsCancellationRequested)
        {
            await host.Hub.PublishDesiredAsync("{\"x\":1}", version: 11);
            var done = await Task.WhenAny(receive, Task.Delay(300, cts.Token));
            if (done == receive)
            {
                version = await receive;
                break;
            }
        }
        await Assert.That(version).IsEqualTo(11L);
    }
}

internal sealed class Reported
{
    public int Battery { get; set; }
}

[JsonSerializable(typeof(Reported))]
internal sealed partial class OverloadJsonContext : JsonSerializerContext
{
}
