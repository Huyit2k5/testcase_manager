namespace Acme.TestCaseManagement.Automation;

/// <summary>Names shared by the API key authentication handler, the permission provider and the host.</summary>
public static class ApiKeyDefaults
{
    /// <summary>The name of the authentication scheme that reads the key.</summary>
    public const string Scheme = "TcmApiKey";

    /// <summary>The request header that carries the key.</summary>
    public const string HeaderName = "X-Api-Key";
}

/// <summary>Claims of the principal that an API key authenticates as.</summary>
public static class ApiKeyClaimTypes
{
    /// <summary>The id of the key. Its presence is what marks the principal as an API key and not a person.</summary>
    public const string ApiKeyId = "tcm_api_key_id";

    public const string ApiKeyName = "tcm_api_key_name";
}
