// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby.Tests;

public sealed class IoTHubConnectionStringTests
{
    [Test]
    public async Task Parses_device_shared_access_key()
    {
        var cs = IoTHubConnectionString.Parse(
            "HostName=my-hub.azure-devices.net;DeviceId=dev1;SharedAccessKey=aGVsbG8=");

        await Assert.That(cs.HostName).IsEqualTo("my-hub.azure-devices.net");
        await Assert.That(cs.DeviceId).IsEqualTo("dev1");
        await Assert.That(cs.ModuleId).IsNull();
        await Assert.That(cs.IsModule).IsFalse();
        await Assert.That(cs.SharedAccessKey).IsEqualTo("aGVsbG8=");
        await Assert.That(cs.AuthMethod).IsEqualTo(IoTHubAuthMethod.SharedAccessKey);
        await Assert.That(cs.ConnectHost).IsEqualTo("my-hub.azure-devices.net");
    }

    [Test]
    public async Task Parses_module_with_gateway()
    {
        var cs = IoTHubConnectionString.Parse(
            "HostName=my-hub.azure-devices.net;DeviceId=dev1;ModuleId=mod1;" +
            "SharedAccessKey=aGVsbG8=;GatewayHostName=edge-gw");

        await Assert.That(cs.ModuleId).IsEqualTo("mod1");
        await Assert.That(cs.IsModule).IsTrue();
        await Assert.That(cs.GatewayHostName).IsEqualTo("edge-gw");
        await Assert.That(cs.ConnectHost).IsEqualTo("edge-gw");
    }

    [Test]
    public async Task Parses_shared_access_signature()
    {
        var cs = IoTHubConnectionString.Parse(
            "HostName=h;DeviceId=d;SharedAccessSignature=SharedAccessSignature sr=abc&sig=xyz&se=123");

        await Assert.That(cs.AuthMethod).IsEqualTo(IoTHubAuthMethod.SharedAccessSignature);
        await Assert.That(cs.SharedAccessSignature).IsEqualTo("SharedAccessSignature sr=abc&sig=xyz&se=123");
    }

    [Test]
    public async Task Parses_x509()
    {
        var cs = IoTHubConnectionString.Parse("HostName=h;DeviceId=d;X509=true");
        await Assert.That(cs.AuthMethod).IsEqualTo(IoTHubAuthMethod.X509);
    }

    [Test]
    public async Task Is_case_insensitive_for_keys()
    {
        var cs = IoTHubConnectionString.Parse("hostname=h;deviceid=d;sharedaccesskey=aGVsbG8=");
        await Assert.That(cs.HostName).IsEqualTo("h");
        await Assert.That(cs.DeviceId).IsEqualTo("d");
    }

    [Test]
    public async Task Throws_when_hostname_missing()
    {
        await Assert.That(() => IoTHubConnectionString.Parse("DeviceId=d;SharedAccessKey=aGVsbG8="))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Throws_when_no_auth_material()
    {
        await Assert.That(() => IoTHubConnectionString.Parse("HostName=h;DeviceId=d"))
            .Throws<FormatException>();
    }
}
