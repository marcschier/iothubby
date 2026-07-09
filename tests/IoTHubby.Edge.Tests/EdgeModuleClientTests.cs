// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby.Edge.Tests;

[NotInParallel("edge-environment")]
public sealed class EdgeModuleClientTests
{
    [Test]
    public async Task Uses_debug_connection_string_when_present()
    {
        Environment.SetEnvironmentVariable(
            "EdgeHubConnectionString",
            "HostName=h.azure-devices.net;DeviceId=dev1;ModuleId=mod1;SharedAccessKey=aGVsbG8=;GatewayHostName=gw");
        try
        {
            await using var client = await EdgeModuleClient.CreateFromEnvironmentAsync();
            await Assert.That(client.State).IsEqualTo(IoTHubConnectionState.Disconnected);
        }
        finally
        {
            Environment.SetEnvironmentVariable("EdgeHubConnectionString", null);
        }
    }

    [Test]
    public async Task Throws_when_no_environment_configured()
    {
        Environment.SetEnvironmentVariable("EdgeHubConnectionString", null);
        Environment.SetEnvironmentVariable("IotHubConnectionString", null);
        Environment.SetEnvironmentVariable("IOTEDGE_IOTHUBHOSTNAME", null);

        await Assert.That(async () => await EdgeModuleClient.CreateFromEnvironmentAsync())
            .Throws<InvalidOperationException>();
    }
}
