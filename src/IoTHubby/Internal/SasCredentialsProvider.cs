// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using Mqtt.Client;

namespace IoTHubby;

/// <summary>
/// Supplies IoT Hub SAS credentials derived from a symmetric key, and proactively triggers a
/// reconnect shortly before the current token expires so a fresh token is presented without an
/// expiry-driven disconnect.
/// </summary>
/// <remarks>
/// Implements <see cref="IMqttCredentialsProvider"/> (consulted on every connect) and
/// <see cref="IMqttCredentialsChangeNotifier"/> (raised on the renewal timer, which the underlying
/// MQTT client observes to reconnect). The username is fixed across renewals; only the SAS password
/// changes.
/// </remarks>
internal sealed class SasCredentialsProvider : IMqttCredentialsProvider, IMqttCredentialsChangeNotifier, IDisposable
{
    private readonly string _username;
    private readonly string _resourceUri;
    private readonly string _base64Key;
    private readonly TimeSpan _lifetime;
    private readonly double _renewalFraction;
    private readonly Timer _renewTimer;
    private int _disposed;

    public SasCredentialsProvider(
        string username,
        string resourceUri,
        string base64Key,
        TimeSpan lifetime,
        double renewalFraction)
    {
        _username = username;
        _resourceUri = resourceUri;
        _base64Key = base64Key;
        _lifetime = lifetime;
        _renewalFraction = renewalFraction is > 0 and <= 1 ? renewalFraction : 0.85;
        _renewTimer = new Timer(
            static state => ((SasCredentialsProvider)state!).OnRenew(),
            this,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public event EventHandler? CredentialsChanged;

    public ValueTask<MqttCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        var expiry = DateTimeOffset.UtcNow + _lifetime;
        var token = SasTokenGenerator.Create(_resourceUri, _base64Key, expiry);

        if (_disposed == 0)
        {
            // Renew after the configured fraction of the token's lifetime has elapsed.
            var renewAfter = TimeSpan.FromTicks((long)(_lifetime.Ticks * _renewalFraction));
            if (renewAfter < TimeSpan.FromSeconds(1))
            {
                renewAfter = TimeSpan.FromSeconds(1);
            }
            _renewTimer.Change(renewAfter, Timeout.InfiniteTimeSpan);
        }

        return new ValueTask<MqttCredentials>(new MqttCredentials(_username, Encoding.UTF8.GetBytes(token)));
    }

    private void OnRenew() => CredentialsChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _renewTimer.Dispose();
    }
}
