using System.Reflection;
using Acme.TestCaseManagement.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Shouldly;
using Volo.Abp.Application.Services;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>
/// The controllers hold no logic of their own: each action forwards to an application service, where the permission
/// checks live. These tests keep it that way, so an endpoint can neither skip authorization nor appear without a
/// matching application service method.
/// </summary>
public class Controller_Conventions_Tests
{
    private static readonly List<Type> Controllers = typeof(TestCaseManagementController).Assembly.GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(TestCaseManagementController).IsAssignableFrom(type))
        .ToList();

    [Fact]
    public void There_Is_One_Controller_Per_Application_Service()
    {
        var services = typeof(TestCaseManagementApplicationContractsModule).Assembly.GetTypes()
            .Where(type => type.IsInterface && type != typeof(IApplicationService) && typeof(IApplicationService).IsAssignableFrom(type))
            .ToList();

        Controllers.Count.ShouldBe(10);
        services.Count.ShouldBe(10);

        foreach (var service in services)
        {
            Controllers.Count(controller => service.IsAssignableFrom(controller)).ShouldBe(1, $"{service.Name} needs exactly one controller");
        }
    }

    [Fact]
    public void Every_Action_Implements_An_Application_Service_Method_And_Nothing_Is_Anonymous()
    {
        foreach (var controller in Controllers)
        {
            controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).ShouldBeEmpty($"{controller.Name} allows anonymous access");

            var contracts = controller.GetInterfaces()
                .Where(type => type != typeof(IApplicationService) && typeof(IApplicationService).IsAssignableFrom(type))
                .ToList();
            var contractMethods = contracts.SelectMany(contract => controller.GetInterfaceMap(contract).TargetMethods).ToHashSet();

            var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
                .ToList();

            actions.ShouldNotBeEmpty(controller.Name);
            foreach (var action in actions)
            {
                action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).ShouldBeEmpty($"{controller.Name}.{action.Name} allows anonymous access");
                contractMethods.ShouldContain(action, $"{controller.Name}.{action.Name} is not an application service method");
            }

            // And the other way round: every application service method is reachable over HTTP.
            contractMethods.Where(method => !actions.Contains(method)).ShouldBeEmpty($"{controller.Name} leaves application service methods without a route");
        }
    }
}
