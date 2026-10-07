using Acme.TestCaseManagement.Permissions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.PermissionManagement.Identity;
using Volo.Abp.Uow;

namespace Acme.TestCaseManagement.Authentication;

/// <summary>
/// Creates the roles of a test team and a demo user for each, with the permissions the role needs. Runs only when
/// Seed:Password is configured, so a host never ends up with accounts that have a well-known password by accident.
/// </summary>
public class HostDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    public const string QaLead = "QA Lead";
    public const string ProductOwner = "Product Owner";
    public const string Tester = "Tester";

    private static readonly string[] AllPermissions = TestCaseManagementPermissions.GetAll()
        .Where(name => name != TestCaseManagementPermissions.GroupName)
        .ToArray();

    /// <summary>Reads everything, maintains requirements, and gives the sign-off approval.</summary>
    private static readonly string[] ProductOwnerPermissions =
    {
        TestCaseManagementPermissions.TestCases.Default, TestCaseManagementPermissions.TestSuites.Default,
        TestCaseManagementPermissions.TestPlans.Default, TestCaseManagementPermissions.TestRuns.Default,
        TestCaseManagementPermissions.Requirements.Default, TestCaseManagementPermissions.Requirements.Manage,
        TestCaseManagementPermissions.QualityGates.Default, TestCaseManagementPermissions.SignOff.Default,
        TestCaseManagementPermissions.SignOff.Approve, TestCaseManagementPermissions.SharedSteps.Default,
    };

    private static readonly string[] TesterPermissions =
    {
        TestCaseManagementPermissions.TestCases.Default, TestCaseManagementPermissions.TestCases.Create,
        TestCaseManagementPermissions.TestCases.Update, TestCaseManagementPermissions.TestSuites.Default,
        TestCaseManagementPermissions.TestPlans.Default, TestCaseManagementPermissions.TestRuns.Default,
        TestCaseManagementPermissions.TestRuns.Execute, TestCaseManagementPermissions.Requirements.Default,
        TestCaseManagementPermissions.QualityGates.Default, TestCaseManagementPermissions.SignOff.Default,
        TestCaseManagementPermissions.SharedSteps.Default,
    };

    private readonly IConfiguration _configuration;
    private readonly IdentityUserManager _users;
    private readonly IdentityRoleManager _roles;
    private readonly IPermissionDataSeeder _permissions;
    private readonly IGuidGenerator _guids;
    private readonly ILogger<HostDataSeedContributor> _logger;

    public HostDataSeedContributor(
        IConfiguration configuration,
        IdentityUserManager users,
        IdentityRoleManager roles,
        IPermissionDataSeeder permissions,
        IGuidGenerator guids,
        ILogger<HostDataSeedContributor> logger)
    {
        _configuration = configuration;
        _users = users;
        _roles = roles;
        _permissions = permissions;
        _guids = guids;
        _logger = logger;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        var password = _configuration["Seed:Password"];
        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("Seed:Password is not configured; no roles or demo users were created.");
            return;
        }

        await EnsureRoleAsync(QaLead, AllPermissions, context);
        await EnsureRoleAsync(ProductOwner, ProductOwnerPermissions, context);
        await EnsureRoleAsync(Tester, TesterPermissions, context);

        await EnsureUserAsync("qa.lead", QaLead, password, context);
        await EnsureUserAsync("product.owner", ProductOwner, password, context);
        await EnsureUserAsync("tester", Tester, password, context);
    }

    private async Task EnsureRoleAsync(string name, string[] permissions, DataSeedContext context)
    {
        if (await _roles.FindByNameAsync(name) is null)
        {
            Succeeded(await _roles.CreateAsync(new IdentityRole(_guids.Create(), name, context.TenantId)));
        }

        await _permissions.SeedAsync(RolePermissionValueProvider.ProviderName, name, permissions, context.TenantId);
    }

    private async Task EnsureUserAsync(string userName, string role, string password, DataSeedContext context)
    {
        if (await _users.FindByNameAsync(userName) is not null)
        {
            return;
        }

        var user = new IdentityUser(_guids.Create(), userName, $"{userName}@example.test", context.TenantId);
        Succeeded(await _users.CreateAsync(user, password));
        Succeeded(await _users.AddToRoleAsync(user, role));
    }

    private static void Succeeded(Microsoft.AspNetCore.Identity.IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Seeding failed: " + string.Join("; ", result.Errors.Select(error => error.Description)));
        }
    }
}
