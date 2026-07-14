namespace FlashSale.ContractTests.Helpers;

/// <summary>
/// Factory that provides a pre-configured <see cref="HttpClient"/> pointing at
/// the .NET app under test (configured via <c>appsettings.Testing.json</c>).
///
/// This class intentionally does NOT use <c>WebApplicationFactory&lt;Program&gt;</c>
/// because:
///   1. We want to test the deployed binary (same process as the running app).
///   2. <c>WebApplicationFactory</c> spins up an in-process test server which would
///      create a second DbContext / Redis connection — undesirable in CI where Docker
///      infra is the source of truth.
///   3. Simpler to maintain: tests hit the same port (5080) that k6 / curl / browser do.
///
/// <para>
/// The base URL is read from <c>TESTING_BASE_URL</c> env var (defaults to
/// <c>http://localhost:5080</c>). In CI with Docker Compose, the service name
/// resolves inside the network (e.g. <c>http://flashsale.api:5080</c>).
/// </para>
/// </summary>
public sealed class ContractTestHttpClient : IDisposable
{
    private readonly HttpClient _client;

    public string BaseUrl { get; }

    public ContractTestHttpClient()
    {
        BaseUrl = Environment.GetEnvironmentVariable("TESTING_BASE_URL")
                  ?? "http://localhost:5080";

        _client = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken ct = default)
        => _client.GetAsync(requestUri, ct);

    public Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent? content = null, CancellationToken ct = default)
        => _client.PostAsync(requestUri, content, ct);

    public Task<HttpResponseMessage> PutAsync(string requestUri, HttpContent? content = null, CancellationToken ct = default)
        => _client.PutAsync(requestUri, content, ct);

    public Task<string> GetStringAsync(string requestUri, CancellationToken ct = default)
        => _client.GetStringAsync(requestUri, ct);

    public void Dispose() => _client.Dispose();
}
