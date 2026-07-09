// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Globalization;

namespace IoTHubby;

/// <summary>
/// A correlated response for a request identified by a <c>$rid</c> value.
/// </summary>
internal readonly struct RidResponse
{
    public RidResponse(int status, byte[] payload, IReadOnlyDictionary<string, string> query)
    {
        Status = status;
        Payload = payload;
        Query = query;
    }

    /// <summary>Status code parsed from the response topic (e.g. 200/204/4xx/5xx).</summary>
    public int Status { get; }

    /// <summary>Response body (a private copy; safe to retain).</summary>
    public byte[] Payload { get; }

    /// <summary>Additional response query parameters (e.g. <c>$version</c>), never <c>null</c>.</summary>
    public IReadOnlyDictionary<string, string> Query { get; }

    public bool IsSuccess => Status is >= 200 and < 300;
}

/// <summary>
/// Allocates monotonically increasing <c>$rid</c> request identifiers and correlates each with the
/// task awaiting its response. Because IoT Hub speaks MQTT 3.1.1 (no MQTT 5 request/response),
/// twin GET/PATCH and DPS register/poll rely on this manual correlation over the shared response
/// subscription.
/// </summary>
internal sealed class RidCorrelator
{
    private static readonly IReadOnlyDictionary<string, string> EmptyQuery =
        new Dictionary<string, string>(0);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<RidResponse>> _pending =
        new(StringComparer.Ordinal);

    private int _next;

    /// <summary>Allocates the next request id.</summary>
    public string Next()
        => Interlocked.Increment(ref _next).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Registers a pending request and returns a task that completes when <see cref="TryComplete"/>
    /// is called with the same <paramref name="rid"/>, or faults on timeout/cancellation.
    /// </summary>
    public async Task<RidResponse> WaitAsync(string rid, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<RidResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(rid, tcs))
        {
            throw new InvalidOperationException($"Duplicate request id '{rid}'.");
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        using (linked.Token.Register(static state =>
        {
            var (self, id) = ((RidCorrelator, string))state!;
            if (self._pending.TryRemove(id, out var pending))
            {
                pending.TrySetCanceled();
            }
        }, (this, rid)))
        {
            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out waiting for response to request '{rid}'.");
            }
        }
    }

    /// <summary>
    /// Completes the pending request for <paramref name="rid"/> with <paramref name="status"/> and a
    /// copy of <paramref name="payload"/>. Returns false when no request is awaiting that id.
    /// </summary>
    public bool TryComplete(
        string rid,
        int status,
        ReadOnlySpan<byte> payload,
        IReadOnlyDictionary<string, string>? query = null)
    {
        if (!_pending.TryRemove(rid, out var tcs))
        {
            return false;
        }

        return tcs.TrySetResult(new RidResponse(status, payload.ToArray(), query ?? EmptyQuery));
    }

    /// <summary>Faults every pending request, e.g. on disconnect.</summary>
    public void FailAll(Exception exception)
    {
        foreach (var rid in _pending.Keys)
        {
            if (_pending.TryRemove(rid, out var tcs))
            {
                tcs.TrySetException(exception);
            }
        }
    }
}
