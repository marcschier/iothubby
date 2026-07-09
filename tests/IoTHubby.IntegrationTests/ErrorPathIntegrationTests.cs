// Copyright (c) marcschier. Licensed under the MIT License.

using IoTHubby.Provisioning;
using Mqtt.Client.Testing;

namespace IoTHubby.IntegrationTests;

public sealed class ErrorPathIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Method_handler_exception_returns_500()
    {
        await using var host = await TestHost.StartAsync();
        await using var device = host.CreateDevice("dev-throw");
        await device.ConnectAsync();

        await device.SetMethodHandlerAsync((_, _) => throw new InvalidOperationException("boom"));

        var result = await host.Hub.InvokeMethodAsync("explode", "{}", Timeout);
        await Assert.That(result.Status).IsEqualTo(500);
    }

    [Test]
    public async Task GetTwin_throws_on_error_status()
    {
        await using var host = await TestHost.StartAsync();
        host.Hub.TwinGetStatus = 404;
        await using var device = host.CreateDevice("dev-twinerr");
        await device.ConnectAsync();

        await Assert.That(async () => await device.GetTwinAsync()).Throws<IoTHubClientException>();
    }

    [Test]
    public async Task Module_twin_get_update_and_method()
    {
        await using var host = await TestHost.StartAsync();
        host.Hub.SetTwin("{\"desired\":{\"i\":5,\"$version\":2},\"reported\":{\"$version\":3}}");
        await using var module = host.CreateModule("dev-mt", "modT");
        await module.ConnectAsync();

        var twin = await module.GetTwinAsync();
        await Assert.That(twin.Desired.Version).IsEqualTo(2L);

        var version = await module.UpdateReportedPropertiesAsync("{\"online\":true}");
        await Assert.That(version).IsNotNull();

        await module.SetMethodHandlerAsync((request, _) =>
            new ValueTask<DirectMethodResponse>(DirectMethodResponse.FromString(200, $"\"{request.Name}\"")));
        var result = await host.Hub.InvokeMethodAsync("ping", "{}", Timeout);
        await Assert.That(result.Status).IsEqualTo(200);
    }

    [Test]
    public async Task Dps_registration_failure_throws()
    {
        await using var broker = await MqttTestBroker.StartAsync();
        await using var dps = await FakeDps.StartAsync(broker.Port, "hub", "dev", fail: true);

        await using var client = ProvisioningClient.CreateWithSymmetricKey(
            "0ne00000000",
            "reg-fail",
            "aGVsbG8=",
            o =>
            {
                o.EndpointHostOverride = "127.0.0.1";
                o.EndpointPortOverride = broker.Port;
                o.DisableTls = true;
                o.Timeout = TimeSpan.FromSeconds(15);
            });

        await Assert.That(async () => await client.RegisterAsync()).Throws<IoTHubClientException>();
    }
}
