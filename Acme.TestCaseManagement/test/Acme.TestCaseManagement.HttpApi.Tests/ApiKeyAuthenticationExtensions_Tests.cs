using Acme.TestCaseManagement.Authentication;
using Acme.TestCaseManagement.Automation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>
/// <c>AddTestCaseManagementApiKeyAuthentication</c> is what a host built from an ABP template calls: its default scheme
/// (Identity.Application) forwards bearer tokens, and the helper adds the API key without changing what it did before.
/// </summary>
public class ApiKeyAuthenticationExtensions_Tests
{
    private const string BearerScheme = "Bearer";

    private static CookieAuthenticationOptions BuildDefaultSchemeOptions(string scheme = "Identity.Application")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services
            .AddAuthentication()
            .AddCookie(scheme, options =>
            {
                // What ABP's ForwardIdentityAuthenticationForBearer does: a bearer token goes to the token validation.
                options.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                        ? BearerScheme
                        : null;
            });
        services.AddTestCaseManagementApiKeyAuthentication(scheme);

        return services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
    }

    private static HttpContext Request(Action<IHeaderDictionary>? headers = null)
    {
        var context = new DefaultHttpContext();
        headers?.Invoke(context.Request.Headers);
        return context;
    }

    [Fact]
    public void A_request_that_names_an_api_key_is_forwarded_to_the_api_key_scheme()
    {
        var options = BuildDefaultSchemeOptions();

        options.ForwardDefaultSelector!(Request(h => h[ApiKeyDefaults.HeaderName] = "tcm_00000000_x")).ShouldBe(ApiKeyDefaults.Scheme);
    }

    [Fact]
    public void Any_other_request_goes_where_it_went_before()
    {
        var options = BuildDefaultSchemeOptions();

        options.ForwardDefaultSelector!(Request(h => h.Authorization = "Bearer abc")).ShouldBe(BearerScheme);
        options.ForwardDefaultSelector!(Request()).ShouldBeNull();
    }

    [Fact]
    public void The_api_key_scheme_is_registered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTestCaseManagementApiKeyAuthentication();

        var scheme = services.BuildServiceProvider().GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(ApiKeyDefaults.Scheme).GetAwaiter().GetResult();

        scheme.ShouldNotBeNull();
        scheme.HandlerType.ShouldBe(typeof(ApiKeyAuthenticationHandler));
    }
}
