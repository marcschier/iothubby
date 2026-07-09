// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace IoTHubby.Provisioning;

internal sealed class RegisterRequest
{
    public string RegistrationId { get; set; } = string.Empty;

    public JsonElement? Payload { get; set; }
}

internal sealed class RegistrationOperationStatus
{
    public string? OperationId { get; set; }

    public string? Status { get; set; }

    public RegistrationState? RegistrationState { get; set; }
}

internal sealed class RegistrationState
{
    public string? AssignedHub { get; set; }

    public string? DeviceId { get; set; }

    public string? RegistrationId { get; set; }

    public string? Status { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(RegistrationOperationStatus))]
internal sealed partial class ProvisioningJsonContext : JsonSerializerContext
{
}
