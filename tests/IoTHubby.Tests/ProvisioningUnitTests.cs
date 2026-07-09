// Copyright (c) marcschier. Licensed under the MIT License.

using IoTHubby.Provisioning;

namespace IoTHubby.Tests;

public sealed class ProvisioningUnitTests
{
    [Test]
    public async Task DpsTopics_build_register_and_poll_topics()
    {
        await Assert.That(DpsTopics.RegisterTopic("3"))
            .IsEqualTo("$dps/registrations/PUT/iotdps-register/?$rid=3");
        await Assert.That(DpsTopics.OperationStatusTopic("4", "op-9"))
            .IsEqualTo("$dps/registrations/GET/iotdps-get-operationstatus/?$rid=4&operationId=op-9");
    }

    [Test]
    public async Task DpsTopics_parse_response_variants()
    {
        var ok = DpsTopics.TryParseResponse(
            "$dps/registrations/res/202/?$rid=1&retry-after=3", out var status, out var rid, out var retry);
        await Assert.That(ok).IsTrue();
        await Assert.That(status).IsEqualTo(202);
        await Assert.That(rid).IsEqualTo("1");
        await Assert.That(retry).IsEqualTo(3);

        var ok200 = DpsTopics.TryParseResponse(
            "$dps/registrations/res/200/?$rid=2", out var status200, out var rid200, out var retry200);
        await Assert.That(ok200).IsTrue();
        await Assert.That(status200).IsEqualTo(200);
        await Assert.That(rid200).IsEqualTo("2");
        await Assert.That(retry200).IsNull();

        await Assert.That(DpsTopics.TryParseResponse("devices/x/messages", out _, out _, out _)).IsFalse();
    }

    [Test]
    public async Task DeviceRegistrationResult_assigned_hub_or_throw()
    {
        var assigned = new DeviceRegistrationResult("assigned", "hub", "dev", "reg", "op");
        await Assert.That(assigned.AssignedHubOrThrow()).IsEqualTo("hub");

        var unassigned = new DeviceRegistrationResult("failed", null, null, "reg", null);
        await Assert.That(() => unassigned.AssignedHubOrThrow()).Throws<IoTHubClientException>();
    }

    [Test]
    public async Task ProvisioningClient_validates_required_arguments()
    {
        await Assert.That(() => ProvisioningClient.CreateWithSymmetricKey("", "reg", "aGVsbG8="))
            .Throws<ArgumentException>();
        await Assert.That(() => ProvisioningClient.CreateWithSymmetricKey("scope", "", "aGVsbG8="))
            .Throws<ArgumentException>();
        await Assert.That(() => ProvisioningClient.CreateWithSymmetricKey("scope", "reg", ""))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task SasTokenGenerator_helpers()
    {
        var expiry = DateTimeOffset.FromUnixTimeSeconds(1700000000);
        var token = SasTokenGenerator.CreateFromKey("h/devices/d", new byte[16], expiry);
        await Assert.That(token).StartsWith("SharedAccessSignature sr=h%2Fdevices%2Fd&sig=");

        await Assert.That(SasTokenGenerator.StringToSign("h/devices/d", 1700000000))
            .IsEqualTo("h%2Fdevices%2Fd\n1700000000");
        await Assert.That(SasTokenGenerator.ProvisioningResourceUri("scope", "reg"))
            .IsEqualTo("scope/registrations/reg");
    }
}
