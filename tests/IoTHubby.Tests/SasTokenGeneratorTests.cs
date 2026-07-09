// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby.Tests;

public sealed class SasTokenGeneratorTests
{
    private static readonly DateTimeOffset Expiry = DateTimeOffset.FromUnixTimeSeconds(1700000000);

    [Test]
    public async Task Produces_known_token_vector()
    {
        // Reference value computed independently (HMACSHA256 over "{escapedResource}\n{expiry}").
        const string expected =
            "SharedAccessSignature sr=myhub.azure-devices.net%2Fdevices%2Fmydevice" +
            "&sig=TLkSQJPbcbGPBRiT9QCtm4AgShLcBSLXbz6XDGZM5NQ%3D&se=1700000000";

        var token = SasTokenGenerator.Create(
            "myhub.azure-devices.net/devices/mydevice",
            "AAAAAAAAAAAAAAAAAAAAAA==",
            Expiry);

        await Assert.That(token).IsEqualTo(expected);
    }

    [Test]
    public async Task Appends_policy_name_when_provided()
    {
        var token = SasTokenGenerator.Create(
            "myhub.azure-devices.net/devices/mydevice",
            "AAAAAAAAAAAAAAAAAAAAAA==",
            Expiry,
            "device");

        await Assert.That(token).EndsWith("&skn=device");
    }

    [Test]
    public async Task Encodes_resource_uri_slashes()
    {
        var token = SasTokenGenerator.Create(
            "h/devices/d/modules/m",
            "AAAAAAAAAAAAAAAAAAAAAA==",
            Expiry);

        await Assert.That(token).Contains("sr=h%2Fdevices%2Fd%2Fmodules%2Fm");
    }

    [Test]
    public async Task Module_resource_uri_is_well_formed()
    {
        await Assert.That(SasTokenGenerator.ModuleResourceUri("h", "d", "m"))
            .IsEqualTo("h/devices/d/modules/m");
        await Assert.That(SasTokenGenerator.DeviceResourceUri("h", "d"))
            .IsEqualTo("h/devices/d");
        await Assert.That(SasTokenGenerator.ProvisioningResourceUri("0ne00", "reg1"))
            .IsEqualTo("0ne00/registrations/reg1");
    }
}
