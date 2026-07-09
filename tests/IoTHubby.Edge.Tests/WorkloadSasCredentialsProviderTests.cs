// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using System.Text;
using System.Text.Json;
using IoTHubby.Edge.Workload;
using Mqtt.Client;

namespace IoTHubby.Edge.Tests;

public sealed class WorkloadSasCredentialsProviderTests
{
    [Test]
    public async Task GetCredentialsAsync_returns_username_and_signed_sas_token()
    {
        const string username = "hub.azure-devices.net/device1/module1/?api-version=2020-07-07";
        const string resourceUri = "hub.azure-devices.net/devices/device1/modules/module1";
        byte[] digest = [251, 255, 0, 1];
        var encodedResource = Uri.EscapeDataString(resourceUri);
        using var handler = new FakeHandler(async (request, cancellationToken) =>
        {
            await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
            await Assert.That(request.RequestUri!.AbsolutePath)
                .IsEqualTo("/modules/module1/genid/gen1/sign");
            await Assert.That(request.RequestUri.Query).IsEqualTo("?api-version=2020-07-07");

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var stringToSign = Encoding.UTF8.GetString(
                Convert.FromBase64String(document.RootElement.GetProperty("data").GetString()!));
            await Assert.That(body).Contains("\"keyId\":\"primary\"");
            await Assert.That(body).Contains("\"algo\":\"HMACSHA256\"");
            await Assert.That(stringToSign).StartsWith($"{encodedResource}\n");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"digest":"{{Convert.ToBase64String(digest)}}"}"""),
            };
        });
        var client = new WorkloadApiClient(new Uri("http://localhost:15580"), "2020-07-07", handler);
        using var provider = new WorkloadSasCredentialsProvider(
            username,
            resourceUri,
            client,
            "module1",
            "gen1",
            TimeSpan.FromHours(1),
            0.85);

        var credentials = await ((IMqttCredentialsProvider)provider).GetCredentialsAsync(CancellationToken.None);

        await Assert.That(credentials.Username).IsEqualTo(username);
        var password = Encoding.UTF8.GetString(credentials.Password!);
        var expectedSignature = Uri.EscapeDataString(Convert.ToBase64String(digest));
        await Assert.That(password)
            .StartsWith($"SharedAccessSignature sr={encodedResource}&sig={expectedSignature}&se=");
        await Assert.That(password).Contains("&se=");
    }

    [Test]
    public async Task Raises_credentials_changed_on_renewal()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"digest":"AQIDBA=="}"""),
        });
        var client = new WorkloadApiClient(new Uri("http://localhost:15580"), "2020-07-07", handler);
        using var provider = new WorkloadSasCredentialsProvider(
            "u",
            "h/devices/dev1/modules/mod1",
            client,
            "mod1",
            "gen1",
            TimeSpan.FromSeconds(1),
            0.001);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ((IMqttCredentialsChangeNotifier)provider).CredentialsChanged += (_, _) => tcs.TrySetResult(true);

        _ = await ((IMqttCredentialsProvider)provider).GetCredentialsAsync(CancellationToken.None);

        var raised = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await Assert.That(raised).IsEqualTo((Task)tcs.Task);
    }

    [Test]
    public async Task Dispose_is_idempotent_and_disposes_workload_client()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"digest":"AQIDBA=="}"""),
        });
        var client = new WorkloadApiClient(new Uri("http://localhost:15580"), "2020-07-07", handler);
        var provider = new WorkloadSasCredentialsProvider(
            "u",
            "h/devices/dev1/modules/mod1",
            client,
            "mod1",
            "gen1",
            TimeSpan.FromHours(1),
            0.85);

        provider.Dispose();
        provider.Dispose();

        await Assert.That(handler.Disposed).IsTrue();
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
            : this((request, _) => Task.FromResult(send(request)))
        {
        }

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        {
            _sendAsync = sendAsync;
        }

        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => _sendAsync(request, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
