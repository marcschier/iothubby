// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby;

/// <summary>
/// Authentication material carried by a parsed IoT Hub connection string.
/// </summary>
internal enum IoTHubAuthMethod
{
    /// <summary>A symmetric key from which SAS tokens are derived (and auto-renewed).</summary>
    SharedAccessKey,

    /// <summary>A pre-computed shared access signature token (not auto-renewable).</summary>
    SharedAccessSignature,

    /// <summary>X.509 client certificate authentication (no password).</summary>
    X509,
}

/// <summary>
/// A parsed Azure IoT Hub / IoT Edge device or module connection string.
/// </summary>
/// <remarks>
/// Recognised, case-insensitive segments (<c>key=value</c> pairs separated by <c>;</c>):
/// <c>HostName</c>, <c>DeviceId</c>, <c>ModuleId</c>, <c>SharedAccessKey</c>,
/// <c>SharedAccessKeyName</c>, <c>SharedAccessSignature</c>, <c>GatewayHostName</c>, and
/// <c>X509</c> (<c>true</c>/<c>false</c>). Values are used verbatim; the caller is responsible for
/// URL-encoding where the wire requires it.
/// </remarks>
internal sealed class IoTHubConnectionString
{
    private IoTHubConnectionString(
        string hostName,
        string deviceId,
        string? moduleId,
        string? sharedAccessKey,
        string? sharedAccessKeyName,
        string? sharedAccessSignature,
        string? gatewayHostName,
        IoTHubAuthMethod authMethod)
    {
        HostName = hostName;
        DeviceId = deviceId;
        ModuleId = moduleId;
        SharedAccessKey = sharedAccessKey;
        SharedAccessKeyName = sharedAccessKeyName;
        SharedAccessSignature = sharedAccessSignature;
        GatewayHostName = gatewayHostName;
        AuthMethod = authMethod;
    }

    /// <summary>IoT Hub host, e.g. <c>my-hub.azure-devices.net</c>.</summary>
    public string HostName { get; }

    /// <summary>Device identity.</summary>
    public string DeviceId { get; }

    /// <summary>Module identity, or <c>null</c> for a device connection.</summary>
    public string? ModuleId { get; }

    /// <summary>Base64 symmetric key, when <see cref="AuthMethod"/> is
    /// <see cref="IoTHubAuthMethod.SharedAccessKey"/>.</summary>
    public string? SharedAccessKey { get; }

    /// <summary>Optional SAS policy name (rarely used on device connections).</summary>
    public string? SharedAccessKeyName { get; }

    /// <summary>Pre-computed SAS token, when <see cref="AuthMethod"/> is
    /// <see cref="IoTHubAuthMethod.SharedAccessSignature"/>.</summary>
    public string? SharedAccessSignature { get; }

    /// <summary>Edge gateway host to connect through, or <c>null</c> to reach the hub directly.</summary>
    public string? GatewayHostName { get; }

    /// <summary>Selected authentication method.</summary>
    public IoTHubAuthMethod AuthMethod { get; }

    /// <summary>True when this connection string identifies a module (not a bare device).</summary>
    public bool IsModule => !string.IsNullOrEmpty(ModuleId);

    /// <summary>
    /// The host the MQTT transport should connect to: the edge gateway when present, otherwise the
    /// hub host.
    /// </summary>
    public string ConnectHost => string.IsNullOrEmpty(GatewayHostName) ? HostName : GatewayHostName!;

    /// <summary>
    /// Builds a connection for an IoT Edge module whose credentials are supplied externally (via the
    /// Workload API). No SAS/key material is stored; the authentication method is a placeholder that
    /// the session ignores because a credentials-provider override is in effect.
    /// </summary>
    internal static IoTHubConnectionString ForEdgeModule(
        string iotHubHost,
        string deviceId,
        string moduleId,
        string? gatewayHostName)
        => new(
            iotHubHost,
            deviceId,
            moduleId,
            null,
            null,
            null,
            gatewayHostName,
            IoTHubAuthMethod.SharedAccessSignature);

    /// <summary>
    /// Parses an IoT Hub connection string. Throws <see cref="FormatException"/> when required
    /// segments are missing or the authentication material is ambiguous.
    /// </summary>
    public static IoTHubConnectionString Parse(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        string? host = null, deviceId = null, moduleId = null, sak = null, sakName = null, sas = null, gateway = null;
        var x509 = false;

        foreach (var segment in connectionString.Split(';'))
        {
            var span = segment.AsSpan().Trim();
            if (span.IsEmpty)
            {
                continue;
            }

            var eq = span.IndexOf('=');
            if (eq <= 0)
            {
                throw new FormatException($"Malformed connection string segment: '{segment}'.");
            }

            var key = span.Slice(0, eq).Trim();
            var value = span.Slice(eq + 1).Trim().ToString();

            if (key.Equals("HostName".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                host = value;
            }
            else if (key.Equals("DeviceId".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                deviceId = value;
            }
            else if (key.Equals("ModuleId".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                moduleId = value;
            }
            else if (key.Equals("SharedAccessKey".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                sak = value;
            }
            else if (key.Equals("SharedAccessKeyName".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                sakName = value;
            }
            else if (key.Equals("SharedAccessSignature".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                sas = value;
            }
            else if (key.Equals("GatewayHostName".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                gateway = value;
            }
            else if (key.Equals("X509".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                x509 = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (string.IsNullOrEmpty(host))
        {
            throw new FormatException("Connection string is missing 'HostName'.");
        }
        if (string.IsNullOrEmpty(deviceId))
        {
            throw new FormatException("Connection string is missing 'DeviceId'.");
        }

        IoTHubAuthMethod auth;
        if (x509)
        {
            auth = IoTHubAuthMethod.X509;
        }
        else if (!string.IsNullOrEmpty(sas))
        {
            auth = IoTHubAuthMethod.SharedAccessSignature;
        }
        else if (!string.IsNullOrEmpty(sak))
        {
            auth = IoTHubAuthMethod.SharedAccessKey;
        }
        else
        {
            throw new FormatException(
                "Connection string must specify one of 'SharedAccessKey', 'SharedAccessSignature', or 'X509=true'.");
        }

        return new IoTHubConnectionString(host!, deviceId!, moduleId, sak, sakName, sas, gateway, auth);
    }
}
