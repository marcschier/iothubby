// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

namespace IoTHubby.IntegrationTests;

[NotInParallel("edge-environment")]
public sealed class EdgeDiIntegrationTests
{
    [Test]
    public async Task AddIoTHubEdgeModuleClient_resolves_and_connects()
    {
        await using var host = await TestHost.StartAsync();
        Environment.SetEnvironmentVariable(
            "EdgeHubConnectionString",
            "HostName=test-hub.azure-devices.net;DeviceId=edge-di;ModuleId=mod;SharedAccessKey=aGVsbG8=");
        try
        {
            var services = new ServiceCollection();
            services.AddIoTHubEdgeModuleClient(o =>
            {
                o.EndpointHostOverride = "127.0.0.1";
                o.EndpointPortOverride = host.Broker.Port;
                o.DisableTls = true;
            });
            await using var provider = services.BuildServiceProvider();

            var module = provider.GetRequiredService<IoTHubModuleClient>();
            await module.ConnectAsync();
            await Assert.That(module.State).IsEqualTo(IoTHubConnectionState.Connected);
        }
        finally
        {
            Environment.SetEnvironmentVariable("EdgeHubConnectionString", null);
        }
    }
}
