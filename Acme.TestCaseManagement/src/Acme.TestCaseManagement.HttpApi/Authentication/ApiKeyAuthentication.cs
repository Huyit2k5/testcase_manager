using System.Security.Claims;
using System.Text.Encodings.Web;
using Acme.TestCaseManagement.Automation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Security.Claims;

namespace Acme.TestCaseManagement.Authentication;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// Authenticates a request that carries an API key in the <c>X-Api-Key</c> header. The principal it builds is not a user:
/// it has no user id and no role, only the claim that marks it as an API key, and the module grants such a principal the
/// single permission to publish automation results (see <c>ApiKeyPermissionValueProvider</c>).
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    public ApiKeyAuthenticationHandler(IOptionsMonitor<ApiKeyAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var secret = Request.Headers[ApiKeyDefaults.HeaderName].ToString().Trim();
        if (secret.Length == 0)
        {
            return AuthenticateResult.NoResult();
        }

        var identity = await Context.RequestServices.GetRequiredService<IApiKeyValidator>().ValidateAsync(secret);
        if (identity == null)
        {
            // The same answer for a key that never existed, one that expired and one that was revoked.
            return AuthenticateResult.Fail("The API key is not valid.");
        }

        var claims = new List<Claim>
        {
            new(ApiKeyClaimTypes.ApiKeyId, identity.Id.ToString()),
            new(ApiKeyClaimTypes.ApiKeyName, identity.Name),
            new(AbpClaimTypes.ClientId, "tcm-api-key:" + identity.Id),
        };

        if (identity.TenantId.HasValue)
        {
            claims.Add(new Claim(AbpClaimTypes.TenantId, identity.TenantId.Value.ToString()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}

public static class ApiKeyAuthenticationExtensions
{
    /// <summary>Adds the API key scheme (<see cref="ApiKeyDefaults.Scheme"/>). The host chooses when it applies, see <see cref="IsApiKeyRequest"/>.</summary>
    public static AuthenticationBuilder AddTestCaseManagementApiKey(this AuthenticationBuilder builder)
    {
        return builder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, displayName: null, configureOptions: _ => { });
    }

    /// <summary>
    /// For a host built from an ABP application template, whose default scheme is the ASP.NET Core Identity scheme
    /// (<c>Identity.Application</c>) that forwards bearer tokens to the token validation: adds the API key scheme and makes
    /// the default scheme forward a request that names an API key to it, and anything else as it did before. Call it
    /// after the host's own authentication set-up (<c>ForwardIdentityAuthenticationForBearer</c>).
    /// </summary>
    public static IServiceCollection AddTestCaseManagementApiKeyAuthentication(this IServiceCollection services, string defaultScheme = "Identity.Application")
    {
        services.AddAuthentication().AddTestCaseManagementApiKey();
        services.PostConfigure<CookieAuthenticationOptions>(defaultScheme, options =>
        {
            var inner = options.ForwardDefaultSelector;
            options.ForwardDefaultSelector = context => context.Request.IsApiKeyRequest() ? ApiKeyDefaults.Scheme : inner?.Invoke(context);
        });
        return services;
    }

    /// <summary>
    /// True when the request names an API key, which a host's policy scheme uses to pick this scheme over a bearer token. An empty
    /// header does not name one: some proxies and CI templates send <c>X-Api-Key:</c> with no value next to a valid bearer token, and
    /// that request must still be authenticated by its token.
    /// </summary>
    public static bool IsApiKeyRequest(this HttpRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.Headers[ApiKeyDefaults.HeaderName].ToString());
    }
}
