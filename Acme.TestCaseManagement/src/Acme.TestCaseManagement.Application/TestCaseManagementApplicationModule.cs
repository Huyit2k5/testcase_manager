using Acme.TestCaseManagement.Automation;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Application;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;

namespace Acme.TestCaseManagement;

[DependsOn(
    typeof(AbpDddApplicationModule),
    typeof(AbpAutoMapperModule),
    typeof(TestCaseManagementDomainModule),
    typeof(TestCaseManagementApplicationContractsModule))]
public class TestCaseManagementApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAutoMapperObjectMapper<TestCaseManagementApplicationModule>();

        // An API key is a principal that is not a user: this provider is what gives it its one permission.
        Configure<AbpPermissionOptions>(options =>
        {
            options.ValueProviders.Add<ApiKeyPermissionValueProvider>();
        });

        Configure<AbpAutoMapperOptions>(options =>
        {
            options.AddMaps<TestCaseManagementApplicationModule>(validate: true);
        });
    }
}
