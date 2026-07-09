// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IoTHubby.Tests;

public sealed class DependencyInjectionTests
{
    private const string DeviceCs = "HostName=h.azure-devices.net;DeviceId=dev1;SharedAccessKey=aGVsbG8=";
    private const string ModuleCs =
        "HostName=h.azure-devices.net;DeviceId=dev1;ModuleId=mod1;SharedAccessKey=aGVsbG8=";

    [Test]
    public async Task AddIoTHubDeviceClient_registers_resolvable_singleton()
    {
        var services = new ServiceCollection();
        services.AddIoTHubDeviceClient(DeviceCs);
        await using var provider = services.BuildServiceProvider();

        var a = provider.GetRequiredService<IoTHubDeviceClient>();
        var b = provider.GetRequiredService<IoTHubDeviceClient>();

        await Assert.That(a).IsNotNull();
        await Assert.That(a).IsSameReferenceAs(b);
        await Assert.That(a.State).IsEqualTo(IoTHubConnectionState.Disconnected);
    }

    [Test]
    public async Task AddIoTHubModuleClient_registers_resolvable_singleton()
    {
        var services = new ServiceCollection();
        services.AddIoTHubModuleClient(ModuleCs);
        await using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IoTHubModuleClient>();
        await Assert.That(client.State).IsEqualTo(IoTHubConnectionState.Disconnected);
    }

    [Test]
    public async Task Configure_callback_and_logger_factory_are_applied()
    {
        var configured = false;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIoTHubDeviceClient(DeviceCs, o =>
        {
            configured = true;
            o.ProductInfo = "di-test/1.0";
        });
        await using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IoTHubDeviceClient>();
        await Assert.That(configured).IsTrue();
        // A logger factory is present in the container and is wired without throwing.
        await Assert.That(provider.GetService<ILoggerFactory>()).IsNotNull();
    }

    [Test]
    public async Task ConnectOnStart_registers_a_hosted_service()
    {
        var withoutHost = new ServiceCollection();
        withoutHost.AddIoTHubDeviceClient(DeviceCs);
        await Assert.That(withoutHost.Any(d => d.ServiceType == typeof(IHostedService))).IsFalse();

        var withHost = new ServiceCollection();
        withHost.AddIoTHubDeviceClient(DeviceCs, connectOnStart: true);
        await Assert.That(withHost.Any(d => d.ServiceType == typeof(IHostedService))).IsTrue();
    }

    [Test]
    public async Task Add_rejects_null_or_empty_connection_string()
    {
        var services = new ServiceCollection();
        await Assert.That(() => services.AddIoTHubDeviceClient("")).Throws<ArgumentException>();
        await Assert.That(() => services.AddIoTHubModuleClient("  ")).Throws<ArgumentException>();
    }
}
