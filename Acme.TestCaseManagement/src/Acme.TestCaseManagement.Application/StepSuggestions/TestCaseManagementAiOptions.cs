namespace Acme.TestCaseManagement.StepSuggestions;

/// <summary>
/// Settings of the built-in step suggestion provider, which calls an OpenAI-compatible chat completions endpoint. The module binds
/// them from the configuration section <see cref="Section"/>, so a host only needs to fill appsettings, user secrets or environment
/// variables. Nothing here is stored in the database, shown on a screen or returned by the API.
/// </summary>
/// <example>
/// <code>
/// "TestCaseManagement": { "AiSuggestions": { "Endpoint": "https://api.openai.com/v1/chat/completions", "ApiKey": "...", "Model": "gpt-4o-mini" } }
/// </code>
/// </example>
public class TestCaseManagementAiOptions
{
    public const string Section = "TestCaseManagement:AiSuggestions";

    /// <summary>
    /// The full URL of the chat completions endpoint, for example <c>https://api.openai.com/v1/chat/completions</c> or, for a model
    /// that runs inside the company, <c>http://ollama-host:11434/v1/chat/completions</c>. Empty turns suggestions off.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>The secret sent with each request. Optional: a model served inside the network often needs none. Keep it in user secrets or a secret store.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The header that carries the key. Empty (the default) sends <c>Authorization: Bearer {key}</c>, which OpenAI and most compatible
    /// servers expect; a service that wants the key in another header, such as Azure OpenAI's <c>api-key</c>, names it here.
    /// </summary>
    public string? ApiKeyHeader { get; set; }

    /// <summary>The model name sent in the request, for example <c>gpt-4o-mini</c> or <c>llama3.1</c>.</summary>
    public string? Model { get; set; }

    /// <summary>How long to wait for the answer before giving up. Default 30 seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>The most bytes read from an answer; a longer one is refused. Default 256 KB.</summary>
    public int MaxResponseBytes { get; set; } = 256 * 1024;

    /// <summary>The instruction given to the model. Empty uses the built-in one, which asks for steps as JSON and to treat the requirement as data.</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>True when the endpoint is a usable http or https address.</summary>
    public bool IsConfigured =>
        Uri.TryCreate(Endpoint?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
