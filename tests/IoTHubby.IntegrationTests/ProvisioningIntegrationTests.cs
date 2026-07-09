// Copyright (c) marcschier. Licensed under the MIT License.

using IoTHubby.Provisioning;
using Mqtt.Client.Testing;

namespace IoTHubby.IntegrationTests;

public sealed class ProvisioningIntegrationTests
{
    [Test]
    public async Task Register_with_symmetric_key_returns_assigned_hub()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var dps = await FakeDps.StartAsync(broker.Port, "assigned-hub.azure-devices.net", "reg-device");

        await using var client = ProvisioningClient.CreateWithSymmetricKey(
            "0ne00000000",
            "reg-device",
            "aGVsbG8=",
            o =>
            {
                o.EndpointHostOverride = "127.0.0.1";
                o.EndpointPortOverride = broker.Port;
                o.DisableTls = true;
                o.Timeout = TimeSpan.FromSeconds(15);
            });

        var result = await client.RegisterAsync();

        await Assert.That(result.Status).IsEqualTo("assigned");
        await Assert.That(result.AssignedHub).IsEqualTo("assigned-hub.azure-devices.net");
        await Assert.That(result.DeviceId).IsEqualTo("reg-device");
        await Assert.That(result.AssignedHubOrThrow()).IsEqualTo("assigned-hub.azure-devices.net");
    }

    [Test]
    public async Task Register_polls_multiple_times_until_assigned()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var dps = await FakeDps.StartAsync(
            broker.Port, "hub2.azure-devices.net", "reg-poll", extraAssigningPolls: 2);

        await using var client = ProvisioningClient.CreateWithSymmetricKey(
            "0ne00000000",
            "reg-poll",
            "aGVsbG8=",
            o =>
            {
                o.EndpointHostOverride = "127.0.0.1";
                o.EndpointPortOverride = broker.Port;
                o.DisableTls = true;
                o.Timeout = TimeSpan.FromSeconds(20);
            });

        var result = await client.RegisterAsync();
        await Assert.That(result.Status).IsEqualTo("assigned");
        await Assert.That(result.AssignedHub).IsEqualTo("hub2.azure-devices.net");
    }
}
