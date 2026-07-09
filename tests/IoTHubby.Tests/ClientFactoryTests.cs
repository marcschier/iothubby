// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby.Tests;

public sealed class ClientFactoryTests
{
    private const string DeviceCs = "HostName=h.azure-devices.net;DeviceId=dev1;SharedAccessKey=aGVsbG8=";
    private const string ModuleCs =
        "HostName=h.azure-devices.net;DeviceId=dev1;ModuleId=mod1;SharedAccessKey=aGVsbG8=";

    [Test]
    public async Task Device_client_builds_from_connection_string()
    {
        await using var client = IoTHubDeviceClient.CreateFromConnectionString(DeviceCs);
        await Assert.That(client.State).IsEqualTo(IoTHubConnectionState.Disconnected);
    }

    [Test]
    public async Task Device_client_rejects_module_connection_string()
    {
        await Assert.That(() => IoTHubDeviceClient.CreateFromConnectionString(ModuleCs))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Module_client_builds_from_connection_string()
    {
        await using var client = IoTHubModuleClient.CreateFromConnectionString(ModuleCs);
        await Assert.That(client.State).IsEqualTo(IoTHubConnectionState.Disconnected);
    }

    [Test]
    public async Task Module_client_rejects_device_connection_string()
    {
        await Assert.That(() => IoTHubModuleClient.CreateFromConnectionString(DeviceCs))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Options_configure_callback_runs()
    {
        var configured = false;
        await using var client = IoTHubDeviceClient.CreateFromConnectionString(DeviceCs, o =>
        {
            configured = true;
            o.ProductInfo = "test/1.0";
        });
        await Assert.That(configured).IsTrue();
    }
}
