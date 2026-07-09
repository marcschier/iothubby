// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace IoTHubby.Edge.Workload;

internal sealed record WorkloadSignRequest(string KeyId, string Algo, string Data);

internal sealed record WorkloadSignResponse(string Digest);

internal sealed record WorkloadTrustBundleResponse(string Certificate);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorkloadSignRequest))]
[JsonSerializable(typeof(WorkloadSignResponse))]
[JsonSerializable(typeof(WorkloadTrustBundleResponse))]
internal sealed partial class WorkloadJsonContext : JsonSerializerContext
{
}
