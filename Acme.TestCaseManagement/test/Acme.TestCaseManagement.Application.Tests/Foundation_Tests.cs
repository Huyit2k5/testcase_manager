using System.Reflection;
using Acme.TestCaseManagement.Permissions;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization.Permissions;
using Xunit;

namespace Acme.TestCaseManagement;

public class Foundation_Tests : TestCaseManagementApplicationTestBase
{
    [Fact]
    public async Task Every_Permission_Constant_Should_Be_Defined()
    {
        var manager = GetRequiredService<IPermissionDefinitionManager>();

        foreach (var name in TestCaseManagementPermissions.GetAll().Where(n => n != TestCaseManagementPermissions.GroupName))
        {
            (await manager.GetOrNullAsync(name)).ShouldNotBeNull($"Permission '{name}' is not defined");
        }
    }

    [Fact]
    public async Task Sign_Off_Approval_Should_Be_A_Child_Of_Sign_Off()
    {
        var manager = GetRequiredService<IPermissionDefinitionManager>();

        var approve = await manager.GetAsync(TestCaseManagementPermissions.SignOff.Approve);
        approve.Parent.ShouldNotBeNull();
        approve.Parent!.Name.ShouldBe(TestCaseManagementPermissions.SignOff.Default);
    }

    [Fact]
    public void Every_Application_Service_Should_Require_A_Permission_And_Allow_No_Anonymous_Access()
    {
        // The feature tests run with AlwaysAllowAuthorization, so this is what keeps a future service from being public.
        var services = typeof(TestCaseManagementApplicationModule).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IApplicationService).IsAssignableFrom(type))
            .ToList();

        services.Count.ShouldBe(17);

        foreach (var service in services)
        {
            service.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ShouldNotBeEmpty($"{service.Name} has no [Authorize]");
            service.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).ShouldBeEmpty($"{service.Name} allows anonymous access");

            foreach (var method in service.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                method.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).ShouldBeEmpty($"{service.Name}.{method.Name} allows anonymous access");
            }
        }
    }

    [Fact]
    public void AutoMapper_Configuration_Should_Be_Valid()
    {
        GetRequiredService<IMapper>().ConfigurationProvider.AssertConfigurationIsValid();
    }
}
