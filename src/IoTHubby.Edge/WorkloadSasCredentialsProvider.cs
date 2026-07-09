// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using IoTHubby.Edge.Workload;
using Mqtt.Client;

namespace IoTHubby.Edge;

/// <summary>
/// Supplies IoT Hub SAS credentials for an IoT Edge module by delegating the HMAC signature to the
/// Edge Workload API (so the module's symmetric key never leaves the runtime). Tokens are renewed
/// proactively before expiry by reconnecting, mirroring the symmetric-key provider in the core SDK.
/// </summary>
internal sealed class WorkloadSasCredentialsProvider :
    IMqttCredentialsProvider,
    IMqttCredentialsChangeNotifier,
    IDisposable
{
    private readonly string _username;
    private readonly string _resourceUri;
    private readonly WorkloadApiClient _workload;
    private readonly string _moduleId;
    private readonly string _generationId;
    private readonly TimeSpan _lifetime;
    private readonly double _renewalFraction;
    private readonly Timer _renewTimer;
    private int _disposed;

    public WorkloadSasCredentialsProvider(
        string username,
        string resourceUri,
        WorkloadApiClient workload,
        string moduleId,
        string generationId,
        TimeSpan lifetime,
        double renewalFraction)
    {
        _username = username;
        _resourceUri = resourceUri;
        _workload = workload;
        _moduleId = moduleId;
        _generationId = generationId;
        _lifetime = lifetime;
        _renewalFraction = renewalFraction is > 0 and <= 1 ? renewalFraction : 0.85;
        _renewTimer = new Timer(
            static state => ((WorkloadSasCredentialsProvider)state!).OnRenew(),
            this,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public event EventHandler? CredentialsChanged;

    public async ValueTask<MqttCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        var expiry = DateTimeOffset.UtcNow + _lifetime;
        var exp = expiry.ToUnixTimeSeconds();
        var encodedResource = Uri.EscapeDataString(_resourceUri);
        var expText = exp.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var stringToSign = $"{encodedResource}\n{expText}";

        var digest = await _workload
            .SignAsync(_moduleId, _generationId, Encoding.UTF8.GetBytes(stringToSign), cancellationToken)
            .ConfigureAwait(false);

        var signature = Uri.EscapeDataString(Convert.ToBase64String(digest));
        var token = $"SharedAccessSignature sr={encodedResource}&sig={signature}&se={expText}";

        if (_disposed == 0)
        {
            var renewAfter = TimeSpan.FromTicks((long)(_lifetime.Ticks * _renewalFraction));
            if (renewAfter < TimeSpan.FromSeconds(1))
            {
                renewAfter = TimeSpan.FromSeconds(1);
            }
            _renewTimer.Change(renewAfter, Timeout.InfiniteTimeSpan);
        }

        return new MqttCredentials(_username, Encoding.UTF8.GetBytes(token));
    }

    private void OnRenew() => CredentialsChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _renewTimer.Dispose();
        _workload.Dispose();
    }
}
