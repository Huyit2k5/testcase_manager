using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Sdk;

namespace Acme.TestCaseManagement;

/// <summary>An HTTP client that acts as one user and sends and receives the module's own DTO types.</summary>
internal sealed class ApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    /// <summary>The seeded demo accounts all share this password (appsettings.Development.json).</summary>
    public const string DemoPassword = "Tcm!Demo123";

    private ApiClient(HttpClient http, Guid userId, string userName)
    {
        _http = http;
        UserId = userId;
        UserName = userName;
    }

    public Guid UserId { get; }

    public string UserName { get; }

    /// <summary>Logs in through POST /api/auth/login and returns a client that sends the bearer token.</summary>
    public static async Task<ApiClient> LoginAsync(
        TestCaseManagementHost host,
        string userName,
        ConcurrentBag<(HttpMethod Method, string Path)>? calls = null,
        string password = DemoPassword)
    {
        var http = host.CreateDefaultClient(new RecordingHandler(calls));

        using var response = await http.PostAsJsonAsync("/api/auth/login", new { userName, password }, JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            throw new XunitException($"Login of {userName} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        var result = (await response.Content.ReadFromJsonAsync<JsonNode>(JsonOptions))!;
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", (string)result["accessToken"]!);

        return new ApiClient(http, Guid.Parse((string)result["userId"]!), userName);
    }

    /// <summary>Asks the API to answer in this language (the Accept-Language header); returns the client for chaining.</summary>
    public ApiClient PreferLanguage(string language)
    {
        _http.DefaultRequestHeaders.AcceptLanguage.Clear();
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        return this;
    }

    /// <summary>A client that sends no credentials at all.</summary>
    public static ApiClient Anonymous(TestCaseManagementHost host) => new(host.CreateDefaultClient(), Guid.Empty, "anonymous");

    /// <summary>A client that sends a bearer token the API cannot have issued.</summary>
    public static ApiClient WithToken(TestCaseManagementHost host, string token)
    {
        var http = host.CreateDefaultClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return new ApiClient(http, Guid.Empty, "forged");
    }

    public Task<T> GetAsync<T>(string url) => SendAsync<T>(HttpMethod.Get, url, null);

    public Task<T> PostAsync<T>(string url, object? body = null) => SendAsync<T>(HttpMethod.Post, url, body);

    public Task<T> PutAsync<T>(string url, object body) => SendAsync<T>(HttpMethod.Put, url, body);

    /// <summary>For endpoints that return no content.</summary>
    public async Task SendAsync(HttpMethod method, string url, object? body = null)
    {
        using var response = await SendRawAsync(method, url, body);
        await EnsureSuccessAsync(response);
    }

    public async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        return await _http.SendAsync(request);
    }

    /// <summary>Uploads a file as multipart form data, with extra form fields, and returns the answer as it is.</summary>
    public async Task<HttpResponseMessage> UploadRawAsync(string url, string fileName, byte[] content, IDictionary<string, string>? fields = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "File", fileName);
        foreach (var (name, value) in fields ?? new Dictionary<string, string>())
        {
            form.Add(new StringContent(value), name);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        return await _http.SendAsync(request);
    }

    /// <summary>Uploads a file and reads the JSON answer.</summary>
    public async Task<T> UploadAsync<T>(string url, string fileName, byte[] content, IDictionary<string, string>? fields = null)
    {
        using var response = await UploadRawAsync(url, fileName, content, fields);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    /// <summary>Downloads a file: its bytes, content type and the file name of the Content-Disposition header.</summary>
    public async Task<(byte[] Bytes, string? ContentType, string? FileName)> DownloadAsync(string url)
    {
        using var response = await SendRawAsync(HttpMethod.Get, url);
        await EnsureSuccessAsync(response);

        var disposition = response.Content.Headers.ContentDisposition;
        return (await response.Content.ReadAsByteArrayAsync(), response.Content.Headers.ContentType?.ToString(), disposition?.FileNameStar ?? disposition?.FileName?.Trim('"'));
    }

    /// <summary>Sends a request that must be rejected and returns the status and the <c>error</c> object of the body.</summary>
    public async Task<(HttpStatusCode Status, JsonNode Error)> SendExpectingErrorAsync(HttpMethod method, string url, object? body = null)
    {
        using var response = await SendRawAsync(method, url, body);
        var text = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            throw new XunitException($"{method} {url} was expected to fail but returned {(int)response.StatusCode}: {text}");
        }

        // A refused permission check is answered by the authentication scheme (401/403) with an empty body.
        if (string.IsNullOrWhiteSpace(text))
        {
            return (response.StatusCode, new JsonObject());
        }

        var error = JsonNode.Parse(text)?["error"];
        return (response.StatusCode, error ?? throw new XunitException($"{method} {url} returned {(int)response.StatusCode} without an error body: {text}"));
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body)
    {
        using var response = await SendRawAsync(method, url, body);
        await EnsureSuccessAsync(response);

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        return result ?? throw new XunitException($"{method} {url} returned an empty body.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var request = response.RequestMessage;
            throw new XunitException(
                $"{request?.Method} {request?.RequestUri?.PathAndQuery} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    /// <summary>Remembers every request so that a test can check which operations of the contract it exercised.</summary>
    private sealed class RecordingHandler : DelegatingHandler
    {
        private readonly ConcurrentBag<(HttpMethod Method, string Path)>? _calls;

        public RecordingHandler(ConcurrentBag<(HttpMethod Method, string Path)>? calls)
        {
            _calls = calls;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _calls?.Add((request.Method, request.RequestUri!.AbsolutePath));
            return base.SendAsync(request, cancellationToken);
        }
    }
}
