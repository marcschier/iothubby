// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace IoTHubby.Edge.Workload;

internal sealed record WorkloadSignRequest(string KeyId, string Algo, string Data);

internal sealed record WorkloadSignResponse(string Digest);

internal sealed record WorkloadEncryptRequest(byte[] Plaintext, byte[] InitializationVector);

internal sealed record WorkloadEncryptResponse(byte[]? Ciphertext);

internal sealed record WorkloadDecryptRequest(byte[] Ciphertext, byte[] InitializationVector);

internal sealed record WorkloadDecryptResponse(byte[]? Plaintext);

internal sealed record WorkloadTrustBundleResponse(string Certificate);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorkloadSignRequest))]
[JsonSerializable(typeof(WorkloadSignResponse))]
[JsonSerializable(typeof(WorkloadEncryptRequest))]
[JsonSerializable(typeof(WorkloadEncryptResponse))]
[JsonSerializable(typeof(WorkloadDecryptRequest))]
[JsonSerializable(typeof(WorkloadDecryptResponse))]
[JsonSerializable(typeof(WorkloadTrustBundleResponse))]
internal sealed partial class WorkloadJsonContext : JsonSerializerContext
{
}
