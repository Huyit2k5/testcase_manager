using System.Security.Claims;
using Acme.TestCaseManagement.Permissions;
using Volo.Abp.Authorization.Permissions;

namespace Acme.TestCaseManagement.Automation;

/// <summary>
/// The permissions of an API key, which are fixed: publishing automation results, and nothing else. A key is not a user
/// and has no role, so no permission can be granted to it in the permission store, and none can be added by mistake.
/// </summary>
public class ApiKeyPermissionValueProvider : PermissionValueProvider
{
    public const string ProviderName = "ApiKey";

    private static readonly HashSet<string> Granted = new(StringComparer.Ordinal)
    {
        TestCaseManagementPermissions.AutomationResults.Publish,
    };

    public ApiKeyPermissionValueProvider(IPermissionStore permissionStore)
        : base(permissionStore)
    {
    }

    public override string Name => ProviderName;

    public override Task<PermissionGrantResult> CheckAsync(PermissionValueCheckContext context)
    {
        return Task.FromResult(IsApiKey(context.Principal) && Granted.Contains(context.Permission.Name)
            ? PermissionGrantResult.Granted
            : PermissionGrantResult.Undefined);
    }

    public override Task<MultiplePermissionGrantResult> CheckAsync(PermissionValuesCheckContext context)
    {
        var apiKey = IsApiKey(context.Principal);
        var result = new MultiplePermissionGrantResult();

        foreach (var permission in context.Permissions)
        {
            result.Result[permission.Name] = apiKey && Granted.Contains(permission.Name)
                ? PermissionGrantResult.Granted
                : PermissionGrantResult.Undefined;
        }

        return Task.FromResult(result);
    }

    private static bool IsApiKey(ClaimsPrincipal? principal)
    {
        return principal?.Identity?.IsAuthenticated == true && principal.HasClaim(c => c.Type == ApiKeyClaimTypes.ApiKeyId);
    }
}
