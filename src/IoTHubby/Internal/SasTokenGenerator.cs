// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;

namespace IoTHubby;

/// <summary>
/// Builds Azure IoT Hub / DPS shared-access-signature (SAS) security tokens.
/// </summary>
/// <remarks>
/// The produced token has the documented form
/// <c>SharedAccessSignature sr={audience}&amp;sig={signature}&amp;se={expiry}[&amp;skn={policy}]</c>,
/// where <c>audience</c> is the URL-encoded resource URI, <c>expiry</c> is the token expiry in
/// seconds since the Unix epoch, and <c>signature</c> is the URL-encoded, Base64-encoded
/// HMAC-SHA256 of <c>{audience}\n{expiry}</c> keyed by the Base64-decoded shared access key.
/// IoT Hub decodes percent-encoding case-insensitively, so the exact hex casing is irrelevant to
/// acceptance; this implementation uses <see cref="Uri.EscapeDataString(string)"/> (RFC 3986,
/// uppercase) which is AOT/trim-safe.
/// </remarks>
internal static class SasTokenGenerator
{
    /// <summary>The resource URI for a device: <c>{host}/devices/{deviceId}</c>.</summary>
    public static string DeviceResourceUri(string host, string deviceId)
        => $"{host}/devices/{deviceId}";

    /// <summary>
    /// The resource URI for a module: <c>{host}/devices/{deviceId}/modules/{moduleId}</c>.
    /// </summary>
    public static string ModuleResourceUri(string host, string deviceId, string moduleId)
        => $"{host}/devices/{deviceId}/modules/{moduleId}";

    /// <summary>The resource URI for a DPS registration: <c>{idScope}/registrations/{registrationId}</c>.</summary>
    public static string ProvisioningResourceUri(string idScope, string registrationId)
        => $"{idScope}/registrations/{registrationId}";

    /// <summary>
    /// Creates a SAS token by signing <paramref name="resourceUri"/> with a Base64-encoded symmetric
    /// key that expires at <paramref name="expiresAtUtc"/>.
    /// </summary>
    /// <param name="resourceUri">Unencoded resource URI (audience).</param>
    /// <param name="base64Key">Base64-encoded shared access key.</param>
    /// <param name="expiresAtUtc">Absolute token expiry.</param>
    /// <param name="policyName">Optional SAS policy name (<c>skn</c>).</param>
    public static string Create(
        string resourceUri,
        string base64Key,
        DateTimeOffset expiresAtUtc,
        string? policyName = null)
    {
        if (string.IsNullOrEmpty(resourceUri))
        {
            throw new ArgumentException("Resource URI is required.", nameof(resourceUri));
        }
        if (string.IsNullOrEmpty(base64Key))
        {
            throw new ArgumentException("Key is required.", nameof(base64Key));
        }

        return CreateFromKey(resourceUri, Convert.FromBase64String(base64Key), expiresAtUtc, policyName);
    }

    /// <summary>
    /// Creates a SAS token by signing <paramref name="resourceUri"/> with an already-decoded key.
    /// Used by the Edge Workload API path where signing is delegated and by callers that hold raw
    /// key bytes.
    /// </summary>
    public static string CreateFromKey(
        string resourceUri,
        byte[] key,
        DateTimeOffset expiresAtUtc,
        string? policyName = null)
    {
        var encodedResource = Uri.EscapeDataString(resourceUri);
        var expiry = expiresAtUtc.ToUnixTimeSeconds();

        var toSign = $"{encodedResource}\n{expiry.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var signature = Sign(key, toSign);
        var encodedSignature = Uri.EscapeDataString(signature);

        var builder = new StringBuilder("SharedAccessSignature sr=", 160)
            .Append(encodedResource)
            .Append("&sig=").Append(encodedSignature)
            .Append("&se=").Append(expiry.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (!string.IsNullOrEmpty(policyName))
        {
            builder.Append("&skn=").Append(Uri.EscapeDataString(policyName!));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Produces the raw Base64 HMAC-SHA256 signature over <c>{encodedAudience}\n{expiry}</c>. Exposed
    /// so the Edge Workload API path can compute the same string-to-sign and delegate the HMAC.
    /// </summary>
    public static string StringToSign(string resourceUri, long expiry)
        => $"{Uri.EscapeDataString(resourceUri)}\n{expiry.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static string Sign(byte[] key, string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
#if NET8_0_OR_GREATER
        Span<byte> hash = stackalloc byte[32];
        HMACSHA256.HashData(key, bytes, hash);
        return Convert.ToBase64String(hash);
#else
        using var hmac = new HMACSHA256(key);
        return Convert.ToBase64String(hmac.ComputeHash(bytes));
#endif
    }
}
