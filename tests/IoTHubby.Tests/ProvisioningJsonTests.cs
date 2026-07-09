// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using IoTHubby.Provisioning;

namespace IoTHubby.Tests;

public sealed class ProvisioningJsonTests
{
    [Test]
    public async Task Deserializes_assigned_registration_status_with_source_generated_context()
    {
        const string json = """
            {
              "operationId": "op-1",
              "status": "assigned",
              "registrationState": {
                "assignedHub": "hub.azure-devices.net",
                "deviceId": "device-1",
                "registrationId": "registration-1",
                "status": "assigned"
              }
            }
            """;

        var status = JsonSerializer.Deserialize(
            json,
            ProvisioningJsonContext.Default.RegistrationOperationStatus);

        await Assert.That(status).IsNotNull();
        await Assert.That(status!.OperationId).IsEqualTo("op-1");
        await Assert.That(status.Status).IsEqualTo("assigned");
        await Assert.That(status.RegistrationState!.AssignedHub).IsEqualTo("hub.azure-devices.net");
        await Assert.That(status.RegistrationState.DeviceId).IsEqualTo("device-1");
    }

    [Test]
    public async Task Serializes_register_request_with_camel_case_names()
    {
        var request = new RegisterRequest { RegistrationId = "registration-1" };
        var json = JsonSerializer.Serialize(request, ProvisioningJsonContext.Default.RegisterRequest);

        await Assert.That(json).Contains("\"registrationId\":\"registration-1\"");
        await Assert.That(json.Contains("\"payload\"", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Serializes_register_request_payload_as_json()
    {
        using var document = JsonDocument.Parse("""{"foo":1}""");
        var request = new RegisterRequest
        {
            RegistrationId = "registration-1",
            Payload = document.RootElement.Clone(),
        };

        var json = JsonSerializer.Serialize(request, ProvisioningJsonContext.Default.RegisterRequest);

        await Assert.That(json).Contains("\"payload\":{\"foo\":1}");
    }
}
