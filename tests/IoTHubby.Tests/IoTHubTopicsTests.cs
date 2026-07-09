// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby.Tests;

public sealed class IoTHubTopicsTests
{
    [Test]
    public async Task Device_telemetry_prefix()
    {
        var t = new IoTHubTopics("dev1");
        await Assert.That(t.TelemetryPrefix).IsEqualTo("devices/dev1/messages/events/");
        await Assert.That(t.CloudToDeviceSubscribe).IsEqualTo("devices/dev1/messages/devicebound/#");
        await Assert.That(t.InputSubscribe).IsNull();
        await Assert.That(t.ClientId).IsEqualTo("dev1");
        await Assert.That(t.IsModule).IsFalse();
    }

    [Test]
    public async Task Module_telemetry_and_input_prefixes()
    {
        var t = new IoTHubTopics("dev1", "mod1");
        await Assert.That(t.TelemetryPrefix).IsEqualTo("devices/dev1/modules/mod1/messages/events/");
        await Assert.That(t.InputSubscribe).IsEqualTo("devices/dev1/modules/mod1/inputs/#");
        await Assert.That(t.ClientId).IsEqualTo("dev1/mod1");
        await Assert.That(t.IsModule).IsTrue();
    }

    [Test]
    public async Task Device_username_includes_api_version()
    {
        var t = new IoTHubTopics("dev1");
        await Assert.That(t.BuildUsername("h.azure-devices.net"))
            .IsEqualTo($"h.azure-devices.net/dev1/?api-version={IoTHubProtocol.ApiVersion}");
    }

    [Test]
    public async Task Module_username_includes_module_id()
    {
        var t = new IoTHubTopics("dev1", "mod1");
        await Assert.That(t.BuildUsername("h.azure-devices.net"))
            .IsEqualTo($"h.azure-devices.net/dev1/mod1/?api-version={IoTHubProtocol.ApiVersion}");
    }

    [Test]
    public async Task Username_appends_product_info()
    {
        var t = new IoTHubTopics("dev1");
        var username = t.BuildUsername("h", "IoTHubby/1.0");
        await Assert.That(username).EndsWith("&DeviceClientType=IoTHubby%2F1.0");
    }

    [Test]
    public async Task Method_and_twin_topics()
    {
        await Assert.That(IoTHubTopics.MethodResponse(200, "5"))
            .IsEqualTo("$iothub/methods/res/200/?$rid=5");
        await Assert.That(IoTHubTopics.TwinGet("7"))
            .IsEqualTo("$iothub/twin/GET/?$rid=7");
        await Assert.That(IoTHubTopics.TwinReportedPatch("9"))
            .IsEqualTo("$iothub/twin/PATCH/properties/reported/?$rid=9");
    }

    [Test]
    public async Task Static_subscription_topics()
    {
        await Assert.That(IoTHubTopics.MethodSubscribe).IsEqualTo("$iothub/methods/POST/#");
        await Assert.That(IoTHubTopics.TwinResponseSubscribe).IsEqualTo("$iothub/twin/res/#");
        await Assert.That(IoTHubTopics.TwinDesiredSubscribe)
            .IsEqualTo("$iothub/twin/PATCH/properties/desired/#");
    }
}
