// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Mqtt.Client;

namespace IoTHubby.Provisioning;

/// <summary>
/// Device Provisioning Service MQTT client for symmetric-key or X.509 individual enrollment.
/// </summary>
public sealed class ProvisioningClient : IAsyncDisposable
{
    private const int DefaultRetryAfterSeconds = 2;
    private const ushort KeepAliveSeconds = 60;
    private const int SubscriptionCapacity = 32;

    private readonly string _idScope;
    private readonly string _registrationId;
    private readonly string? _symmetricKeyBase64;
    private readonly X509Certificate2? _certificate;
    private readonly ProvisioningClientOptions _options;
    private readonly RidCorrelator _correlator = new();

    private MqttClient? _client;
    private MqttSubscription? _subscription;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;
    private int _registerCalled;
    private bool _disposed;

    private ProvisioningClient(
        string idScope,
        string registrationId,
        string? symmetricKeyBase64,
        X509Certificate2? certificate,
        ProvisioningClientOptions options)
    {
        _idScope = idScope;
        _registrationId = registrationId;
        _symmetricKeyBase64 = symmetricKeyBase64;
        _certificate = certificate;
        _options = options;
    }

    /// <summary>
    /// Creates a DPS client that authenticates with a symmetric enrollment key.
    /// </summary>
    /// <param name="idScope">The DPS ID scope.</param>
    /// <param name="registrationId">The registration ID.</param>
    /// <param name="symmetricKeyBase64">The Base64-encoded symmetric enrollment key.</param>
    /// <param name="configure">Optional callback for configuring DPS client options.</param>
    /// <returns>A DPS provisioning client.</returns>
    public static ProvisioningClient CreateWithSymmetricKey(
        string idScope,
        string registrationId,
        string symmetricKeyBase64,
        Action<ProvisioningClientOptions>? configure = null)
    {
        ValidateRequired(idScope, nameof(idScope), "DPS ID scope is required.");
        ValidateRequired(registrationId, nameof(registrationId), "Registration ID is required.");
        ValidateRequired(symmetricKeyBase64, nameof(symmetricKeyBase64), "Symmetric key is required.");

        return new ProvisioningClient(
            idScope,
            registrationId,
            symmetricKeyBase64,
            certificate: null,
            CreateOptions(configure));
    }

    /// <summary>
    /// Creates a DPS client that authenticates with an X.509 client certificate.
    /// </summary>
    /// <param name="idScope">The DPS ID scope.</param>
    /// <param name="registrationId">The registration ID.</param>
    /// <param name="certificate">The client certificate used for X.509 enrollment authentication.</param>
    /// <param name="configure">Optional callback for configuring DPS client options.</param>
    /// <returns>A DPS provisioning client.</returns>
    public static ProvisioningClient CreateWithClientCertificate(
        string idScope,
        string registrationId,
        X509Certificate2 certificate,
        Action<ProvisioningClientOptions>? configure = null)
    {
        ValidateRequired(idScope, nameof(idScope), "DPS ID scope is required.");
        ValidateRequired(registrationId, nameof(registrationId), "Registration ID is required.");
        if (certificate is null)
        {
            throw new ArgumentNullException(nameof(certificate));
        }

        return new ProvisioningClient(
            idScope,
            registrationId,
            symmetricKeyBase64: null,
            certificate,
            CreateOptions(configure));
    }

    /// <summary>
    /// Connects to DPS, sends the register request, polls until registration is terminal, and returns the result.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels registration.</param>
    /// <returns>The terminal DPS registration result.</returns>
    public async Task<DeviceRegistrationResult> RegisterAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _registerCalled, 1) != 0)
        {
            throw new InvalidOperationException("ProvisioningClient.RegisterAsync can be called only once.");
        }

        using var timeoutCts = new CancellationTokenSource(_options.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var effectiveToken = linkedCts.Token;

        try
        {
            await ConnectAndSubscribeAsync(effectiveToken).ConfigureAwait(false);
            var response = await PublishRegisterAsync(effectiveToken).ConfigureAwait(false);
            return await WaitForTerminalResultAsync(response, effectiveToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            throw new IoTHubClientException("DPS registration timed out.", ex);
        }
        catch (TimeoutException ex)
        {
            throw new IoTHubClientException("DPS registration timed out.", ex);
        }
        catch (JsonException ex)
        {
            throw new IoTHubClientException("DPS returned invalid JSON.", ex);
        }
    }

    /// <summary>
    /// Releases the MQTT connection and registration response subscription.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _pumpCts?.Cancel();
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync().ConfigureAwait(false);
        }
        await SafeAwait(_pumpTask).ConfigureAwait(false);
        _correlator.FailAll(new ObjectDisposedException(nameof(ProvisioningClient)));
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
        _pumpCts?.Dispose();
    }

    private static ProvisioningClientOptions CreateOptions(Action<ProvisioningClientOptions>? configure)
    {
        var options = new ProvisioningClientOptions();
        configure?.Invoke(options);
        ValidateRequired(options.GlobalEndpoint, nameof(options.GlobalEndpoint), "DPS global endpoint is required.");
        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Timeout must be positive.");
        }
        if (options.SasTokenLifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("SAS token lifetime must be positive.");
        }

        return options;
    }

    private static void ValidateRequired(string value, string parameterName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    private async Task ConnectAndSubscribeAsync(CancellationToken cancellationToken)
    {
        var mqttOptions = CreateMqttOptions();
        var client = new MqttClient(mqttOptions, _options.LoggerFactory);
        _client = client;

        var result = await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new IoTHubClientException(
                $"DPS rejected the connection (reason code 0x{(byte)result.ReasonCode:X2}).");
        }

        var subscription = await client.SubscribeAsync(
            DpsTopics.ResponseSubscribe,
            new MqttSubscriptionOptions
            {
                QoS = (MqttQoS)IoTHubQoS.AtLeastOnce,
                Capacity = SubscriptionCapacity,
            },
            cancellationToken).ConfigureAwait(false);
        _subscription = subscription;
        _pumpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pumpTask = Task.Run(() => PumpResponsesAsync(subscription, _pumpCts.Token), CancellationToken.None);
    }

    private MqttClientOptions CreateMqttOptions()
    {
        var username = $"{_idScope}/registrations/{_registrationId}/"
            + $"api-version={IoTHubProtocol.ProvisioningApiVersion}";
        var tls = new SslClientAuthenticationOptions { TargetHost = _options.GlobalEndpoint };
        byte[]? password = null;
        if (_symmetricKeyBase64 is not null)
        {
            var resourceUri = SasTokenGenerator.ProvisioningResourceUri(_idScope, _registrationId);
            var token = SasTokenGenerator.Create(
                resourceUri,
                _symmetricKeyBase64,
                DateTimeOffset.UtcNow.Add(_options.SasTokenLifetime));
            password = Encoding.UTF8.GetBytes(token);
        }
        else
        {
            tls.ClientCertificates = new X509CertificateCollection { _certificate! };
        }

        _options.ConfigureTls?.Invoke(tls);

        // Test seam: point the transport at a loopback broker over plain TCP. Never used in production.
        var transportHost = string.IsNullOrEmpty(_options.EndpointHostOverride)
            ? _options.GlobalEndpoint
            : _options.EndpointHostOverride!;
        var port = _options.EndpointPortOverride ?? IoTHubProtocol.SecureMqttPort;
        var transport = _options.DisableTls ? MqttTransportType.Tcp : MqttTransportType.Tls;

        return new MqttClientOptions
        {
            Host = transportHost,
            Port = port,
            Transport = transport,
            ProtocolVersion = MqttProtocolVersion.V311,
            ClientId = _registrationId,
            CleanStart = true,
            KeepAliveSeconds = KeepAliveSeconds,
            Username = username,
            Password = password,
            Tls = tls,
        };
    }

    private async Task<RidResponse> PublishRegisterAsync(CancellationToken cancellationToken)
    {
        var rid = _correlator.Next();
        var payload = CreateRegisterPayload();
        return await PublishAndWaitAsync(DpsTopics.RegisterTopic(rid), rid, payload, cancellationToken)
            .ConfigureAwait(false);
    }

    private ReadOnlyMemory<byte> CreateRegisterPayload()
    {
        var payloadJson = _options.Payload;
        using var payloadDocument = string.IsNullOrWhiteSpace(payloadJson) ? null : JsonDocument.Parse(payloadJson!);
        var request = new RegisterRequest
        {
            RegistrationId = _registrationId,
            Payload = payloadDocument?.RootElement.Clone(),
        };

        return JsonSerializer.SerializeToUtf8Bytes(request, ProvisioningJsonContext.Default.RegisterRequest);
    }

    private async Task<DeviceRegistrationResult> WaitForTerminalResultAsync(
        RidResponse response,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (response.Status >= 300)
            {
                throw new IoTHubClientException(
                    $"DPS registration failed with status {response.Status}.",
                    response.Status);
            }

            var operation = ParseOperationStatus(response.Payload);
            ThrowIfTerminalFailure(operation, response.Status);
            if (response.Status == 200)
            {
                return CreateResult(operation);
            }

            if (response.Status != 202)
            {
                throw new IoTHubClientException(
                    $"DPS registration returned unexpected status {response.Status}.",
                    response.Status);
            }

            var operationId = operation.OperationId;
            if (string.IsNullOrEmpty(operationId))
            {
                throw new IoTHubClientException("DPS assigning response did not include an operation ID.");
            }

            await Task.Delay(GetRetryAfter(response), cancellationToken).ConfigureAwait(false);
            response = await PublishOperationStatusAsync(operationId!, cancellationToken).ConfigureAwait(false);
        }
    }

    private static RegistrationOperationStatus ParseOperationStatus(byte[] payload)
        => JsonSerializer.Deserialize(payload, ProvisioningJsonContext.Default.RegistrationOperationStatus)
            ?? throw new IoTHubClientException("DPS returned an empty registration status.");

    private static void ThrowIfTerminalFailure(RegistrationOperationStatus operation, int statusCode)
    {
        if (IsFailureStatus(operation.Status) || IsFailureStatus(operation.RegistrationState?.Status))
        {
            var status = operation.RegistrationState?.Status ?? operation.Status ?? "failed";
            throw new IoTHubClientException($"DPS registration ended with status '{status}'.", statusCode);
        }
    }

    private static bool IsFailureStatus(string? status)
        => string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "disabled", StringComparison.OrdinalIgnoreCase);

    private DeviceRegistrationResult CreateResult(RegistrationOperationStatus operation)
    {
        var state = operation.RegistrationState;
        return new DeviceRegistrationResult(
            state?.Status ?? operation.Status ?? string.Empty,
            state?.AssignedHub,
            state?.DeviceId,
            state?.RegistrationId ?? _registrationId,
            operation.OperationId);
    }

    private async Task<RidResponse> PublishOperationStatusAsync(string operationId, CancellationToken cancellationToken)
    {
        var rid = _correlator.Next();
        return await PublishAndWaitAsync(
            DpsTopics.OperationStatusTopic(rid, operationId),
            rid,
            ReadOnlyMemory<byte>.Empty,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<RidResponse> PublishAndWaitAsync(
        string topic,
        string rid,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var client = _client ?? throw new ObjectDisposedException(nameof(ProvisioningClient));
        var wait = _correlator.WaitAsync(rid, _options.Timeout, cancellationToken);
        var result = await client.PublishAsync(
            topic,
            payload,
            (MqttQoS)IoTHubQoS.AtLeastOnce,
            retain: false,
            properties: null,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            var exception = new IoTHubClientException(
                $"DPS publish was rejected (reason code 0x{(byte)result.ReasonCode:X2}).");
            _correlator.FailAll(exception);
            throw exception;
        }

        return await wait.ConfigureAwait(false);
    }

    private static TimeSpan GetRetryAfter(RidResponse response)
    {
        var seconds = response.Query.TryGetValue("retry-after", out var value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : DefaultRetryAfterSeconds;
        return TimeSpan.FromSeconds(Math.Max(0, seconds));
    }

    private void ThrowIfDisposed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ProvisioningClient));
        }
#endif
    }

    private async Task PumpResponsesAsync(MqttSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in subscription.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                using (message)
                {
                    if (DpsTopics.TryParseResponse(
                        message.Topic,
                        out var status,
                        out var rid,
                        out var retryAfterSeconds))
                    {
                        _correlator.TryComplete(
                            rid,
                            status,
                            message.PayloadMemory.Span,
                            CreateQuery(retryAfterSeconds));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _disposed)
        {
        }
        catch (Exception ex)
        {
            _correlator.FailAll(ex);
        }
    }

    private static Dictionary<string, string>? CreateQuery(int? retryAfterSeconds)
    {
        if (retryAfterSeconds is null)
        {
            return null;
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["retry-after"] = retryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static async Task SafeAwait(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Pump loops end when the subscription channel completes; ignore teardown races.
        }
    }
}
