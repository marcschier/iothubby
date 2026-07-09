// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby.Provisioning;

/// <summary>
/// The terminal Device Provisioning Service registration result.
/// </summary>
public sealed class DeviceRegistrationResult
{
    internal DeviceRegistrationResult(
        string status,
        string? assignedHub,
        string? deviceId,
        string registrationId,
        string? operationId)
    {
        Status = status;
        AssignedHub = assignedHub;
        DeviceId = deviceId;
        RegistrationId = registrationId;
        OperationId = operationId;
    }

    /// <summary>
    /// The DPS registration status, such as <c>assigned</c>.
    /// </summary>
    public string Status { get; }

    /// <summary>
    /// The assigned IoT Hub host name, when registration completed with an assignment.
    /// </summary>
    public string? AssignedHub { get; }

    /// <summary>
    /// The assigned device identity, when registration completed with an assignment.
    /// </summary>
    public string? DeviceId { get; }

    /// <summary>
    /// The registration identity sent to DPS.
    /// </summary>
    public string RegistrationId { get; }

    /// <summary>
    /// The DPS operation identifier returned by register or poll responses, when present.
    /// </summary>
    public string? OperationId { get; }

    /// <summary>
    /// Returns <see cref="AssignedHub"/> or throws when DPS did not assign a hub.
    /// </summary>
    /// <returns>The assigned IoT Hub host name.</returns>
    public string AssignedHubOrThrow()
    {
        if (AssignedHub is null)
        {
            throw new IoTHubClientException("DPS registration did not return an assigned hub.");
        }

        return AssignedHub;
    }
}
