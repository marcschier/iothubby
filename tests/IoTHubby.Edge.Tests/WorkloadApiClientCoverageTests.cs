// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using IoTHubby.Edge.Workload;

namespace IoTHubby.Edge.Tests;

[NotInParallel("edge-environment")]
public sealed class WorkloadApiClientCoverageTests
{
    [Test]
    public async Task GetTrustBundleCertificatesAsync_returns_single_certificate()
    {
        var pem = CreateCertificatePem("single");
        using var handler = new FakeHandler(async (request, _) =>
        {
            await AssertRequestAsync(request, HttpMethod.Get, "/trust-bundle", "?api-version=2020-07-07");
            return JsonResponse(new { certificate = pem });
        });
        using var client = new WorkloadApiClient(new Uri("http://localhost:15580"), "2020-07-07", handler);

        var certificates = await client.GetTrustBundleCertificatesAsync(CancellationToken.None);

        await Assert.That(certificates.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetTrustBundleCertificatesAsync_returns_concatenated_certificates()
    {
        var pem = $"{CreateCertificatePem("one")}\n{CreateCertificatePem("two")}";
        using var handler = new FakeHandler(async (request, _) =>
        {
            await AssertRequestAsync(request, HttpMethod.Get, "/trust-bundle", "?api-version=2020-07-07");
            return JsonResponse(new { certificate = pem });
        });
        using var client = new WorkloadApiClient(new Uri("http://localhost:15580"), "2020-07-07", handler);

        var certificates = await client.GetTrustBundleCertificatesAsync(CancellationToken.None);

        await Assert.That(certificates.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SignAsync_and_GetTrustBundleAsync_throw_for_server_errors()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var client = new WorkloadApiClient(new Uri("http://localhost:15580"), "2020-07-07", handler);

        await Assert.That(async () => await client.GetTrustBundleAsync(CancellationToken.None))
            .Throws<HttpRequestException>();
        await Assert.That(async () => await client.SignAsync("module1", "gen1", [1, 2, 3], CancellationToken.None))
            .Throws<HttpRequestException>();
    }

    [Test]
    public async Task SignAsync_throws_when_digest_is_missing()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
        });
        using var client = new WorkloadApiClient(new Uri("http://localhost:15580"), "2020-07-07", handler);

        await Assert.That(async () => await client.SignAsync("module1", "gen1", [1, 2, 3], CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Requests_are_built_for_supported_workload_uri_shapes()
    {
        var workloadUris = new[]
        {
            new Uri("http://localhost:15580"),
            new Uri("unix:///var/run/iotedge/workload.sock"),
            new Uri("/var/run/x.sock", UriKind.RelativeOrAbsolute),
        };

        foreach (var workloadUri in workloadUris)
        {
            var signCalls = 0;
            var trustBundleCalls = 0;
            using var handler = new FakeHandler(async (request, _) =>
            {
                await Assert.That(request.RequestUri!.Query).IsEqualTo("?api-version=2020-07-07");
                if (request.Method == HttpMethod.Post)
                {
                    signCalls++;
                    await Assert.That(request.RequestUri.AbsolutePath)
                        .IsEqualTo("/modules/module-id/genid/gen-id/sign");
                    return JsonResponse(new { digest = "AQIDBA==" });
                }

                trustBundleCalls++;
                await Assert.That(request.Method).IsEqualTo(HttpMethod.Get);
                await Assert.That(request.RequestUri.AbsolutePath).IsEqualTo("/trust-bundle");
                return JsonResponse(new { certificate = "pem" });
            });
            using var client = new WorkloadApiClient(workloadUri, "2020-07-07", handler);

            var digest = await client.SignAsync("module-id", "gen-id", [1, 2, 3], CancellationToken.None);
            var trustBundle = await client.GetTrustBundleAsync(CancellationToken.None);

            await Assert.That(Convert.ToBase64String(digest)).IsEqualTo("AQIDBA==");
            await Assert.That(trustBundle).IsEqualTo("pem");
            await Assert.That(signCalls).IsEqualTo(1);
            await Assert.That(trustBundleCalls).IsEqualTo(1);
        }
    }

    [Test]
    public async Task FromEnvironment_returns_usable_client_when_variables_are_set()
    {
        var previousWorkloadUri = Environment.GetEnvironmentVariable("IOTEDGE_WORKLOADURI");
        var previousApiVersion = Environment.GetEnvironmentVariable("IOTEDGE_APIVERSION");

        try
        {
            Environment.SetEnvironmentVariable("IOTEDGE_WORKLOADURI", "/var/run/iotedge/workload.sock");
            Environment.SetEnvironmentVariable("IOTEDGE_APIVERSION", "2020-07-07");

            using var handler = new FakeHandler(async (request, _) =>
            {
                await AssertRequestAsync(request, HttpMethod.Get, "/trust-bundle", "?api-version=2020-07-07");
                return JsonResponse(new { certificate = "pem" });
            });
            using var client = WorkloadApiClient.FromEnvironment(handler);

            var trustBundle = await client.GetTrustBundleAsync(CancellationToken.None);

            await Assert.That(trustBundle).IsEqualTo("pem");
        }
        finally
        {
            Environment.SetEnvironmentVariable("IOTEDGE_WORKLOADURI", previousWorkloadUri);
            Environment.SetEnvironmentVariable("IOTEDGE_APIVERSION", previousApiVersion);
        }
    }

    [Test]
    public async Task FromEnvironment_throws_when_variables_are_missing()
    {
        var previousWorkloadUri = Environment.GetEnvironmentVariable("IOTEDGE_WORKLOADURI");
        var previousApiVersion = Environment.GetEnvironmentVariable("IOTEDGE_APIVERSION");

        try
        {
            Environment.SetEnvironmentVariable("IOTEDGE_WORKLOADURI", null);
            Environment.SetEnvironmentVariable("IOTEDGE_APIVERSION", null);

            using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
            await Assert.That(() => WorkloadApiClient.FromEnvironment(handler))
                .Throws<InvalidOperationException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("IOTEDGE_WORKLOADURI", previousWorkloadUri);
            Environment.SetEnvironmentVariable("IOTEDGE_APIVERSION", previousApiVersion);
        }
    }

    private static string CreateCertificatePem(string commonName)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={commonName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        return certificate.ExportCertificatePem();
    }

    private static HttpResponseMessage JsonResponse(object value)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value)),
        };

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
