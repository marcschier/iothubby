// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net.Security;
using Microsoft.Extensions.Logging;

namespace IoTHubby.Provisioning;

/// <summary>
/// Options for Device Provisioning Service MQTT registration.
/// </summary>
public sealed class ProvisioningClientOptions
{
    /// <summary>
    /// Creates options with DPS defaults.
    /// </summary>
    public ProvisioningClientOptions()
    {
    }

    /// <summary>
    /// The DPS global endpoint host. Defaults to the Azure public global provisioning endpoint.
    /// </summary>
    public string GlobalEndpoint { get; set; } = IoTHubProtocol.GlobalProvisioningHost;

    /// <summary>
    /// The maximum time allowed for connect, register, and polling to complete.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// The lifetime of the SAS token created for symmetric-key registration.
    /// </summary>
    public TimeSpan SasTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Optional logger factory passed to the MQTT transport.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// Optional JSON payload included in the DPS register request as the <c>payload</c> property.
    /// </summary>
    public string? Payload { get; set; }

    /// <summary>
    /// Optional callback for customizing TLS authentication settings before connecting.
    /// </summary>
    public Action<SslClientAuthenticationOptions>? ConfigureTls { get; set; }

    /// <summary>Test-only seam: overrides the transport host (e.g. a loopback broker).</summary>
    internal string? EndpointHostOverride { get; set; }

    /// <summary>Test-only seam: overrides the transport port.</summary>
    internal int? EndpointPortOverride { get; set; }

    /// <summary>Test-only seam: connects over plain TCP instead of TLS (loopback broker).</summary>
    internal bool DisableTls { get; set; }
}
