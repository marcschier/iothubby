// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace IoTHubby.Tests;

public sealed class SessionConstructionTests
{
    private static X509Certificate2 CreateCert()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=iothubby-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    [Test]
    public async Task Device_client_with_x509_certificate_constructs()
    {
        using var cert = CreateCert();
        await using var device = IoTHubDeviceClient.CreateWithClientCertificate(
            "h.azure-devices.net", "dev-x509", cert);
        await Assert.That(device.State).IsEqualTo(IoTHubConnectionState.Disconnected);
    }

    [Test]
    public async Task Module_client_with_x509_certificate_constructs()
    {
        using var cert = CreateCert();
        await using var module = IoTHubModuleClient.CreateWithClientCertificate(
            "h.azure-devices.net", "dev-x509", "mod-x509", cert);
        await Assert.That(module.State).IsEqualTo(IoTHubConnectionState.Disconnected);
    }

    [Test]
    public async Task Client_with_reconnect_disabled_constructs()
    {
        await using var device = IoTHubDeviceClient.CreateFromConnectionString(
            "HostName=h.azure-devices.net;DeviceId=dev;SharedAccessKey=aGVsbG8=",
            o =>
            {
                o.AutoReconnect = false;
                o.KeepAlive = TimeSpan.Zero;
                o.ModelId = "dtmi:example:thing;1";
                o.ProductInfo = "iothubby-tests/1.0";
            });
        await Assert.That(device.State).IsEqualTo(IoTHubConnectionState.Disconnected);
    }
}
