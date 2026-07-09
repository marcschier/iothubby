// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using IoTHubby.Edge.Workload;

namespace IoTHubby.Edge.Tests;

public sealed class WorkloadApiClientTests
{
    [Test]
    public async Task SignAsync_sends_sign_request_and_decodes_digest()
    {
        byte[] data = [1, 2, 3, 4];
        byte[] digest = [16, 32, 48];
        using var handler = new FakeHandler(async (request, cancellationToken) =>
        {
            await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
            await Assert.That(request.RequestUri!.AbsolutePath)
                .IsEqualTo("/modules/module%20one/genid/gen-1/sign");
            await Assert.That(request.RequestUri.Query).IsEqualTo("?api-version=2020-07-07");

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            await Assert.That(root.GetProperty("keyId").GetString()).IsEqualTo("primary");
            await Assert.That(root.GetProperty("algo").GetString()).IsEqualTo("HMACSHA256");
            await Assert.That(root.GetProperty("data").GetString()).IsEqualTo(Convert.ToBase64String(data));

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"digest":"{{Convert.ToBase64String(digest)}}"}"""),
            };
        });
        using var client = new WorkloadApiClient(
            new Uri("unix:///var/run/iotedge/workload.sock"),
            "2020-07-07",
            handler);

        var actual = await client.SignAsync("module one", "gen-1", data, CancellationToken.None);

        await Assert.That(Convert.ToBase64String(actual)).IsEqualTo(Convert.ToBase64String(digest));
    }

    [Test]
    public async Task GetTrustBundleAsync_returns_certificate_pem()
    {
        const string pem = "-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----";
        using var handler = new FakeHandler(async (request, _) =>
        {
            await AssertRequestAsync(request, HttpMethod.Get, "/trust-bundle", "?api-version=2020-07-07");
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"certificate\":\"-----BEGIN CERTIFICATE-----\\nMIIB\\n-----END CERTIFICATE-----\"}"),
            };
        });
        using var client = new WorkloadApiClient(new Uri("http://localhost/"), "2020-07-07", handler);

        var actual = await client.GetTrustBundleAsync(CancellationToken.None);

        await Assert.That(actual).IsEqualTo(pem);
    }

    [Test]
    public async Task FromEnvironment_throws_when_workload_uri_is_missing()
    {
        var previousWorkloadUri = Environment.GetEnvironmentVariable("IOTEDGE_WORKLOADURI");
        var previousApiVersion = Environment.GetEnvironmentVariable("IOTEDGE_APIVERSION");

        try
        {
            Environment.SetEnvironmentVariable("IOTEDGE_WORKLOADURI", null);
            Environment.SetEnvironmentVariable("IOTEDGE_APIVERSION", "2020-07-07");

            await Assert.That(() => WorkloadApiClient.FromEnvironment(new FakeHandler(_ =>
                new HttpResponseMessage(System.Net.HttpStatusCode.OK))))
                .Throws<InvalidOperationException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("IOTEDGE_WORKLOADURI", previousWorkloadUri);
            Environment.SetEnvironmentVariable("IOTEDGE_APIVERSION", previousApiVersion);
        }
    }

    private static async Task AssertRequestAsync(
        HttpRequestMessage request,
        HttpMethod method,
        string absolutePath,
        string query)
    {
        await Assert.That(request.Method).IsEqualTo(method);
        await Assert.That(request.RequestUri!.AbsolutePath).IsEqualTo(absolutePath);
        await Assert.That(request.RequestUri.Query).IsEqualTo(query);
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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => _sendAsync(request, cancellationToken);
    }
}
